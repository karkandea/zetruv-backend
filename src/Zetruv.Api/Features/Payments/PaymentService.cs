using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Orders;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Payments;

public sealed record InitiatePaymentResponse(
    Guid OrderId,
    string OrderNumber,
    string Provider,
    string ProviderReference,
    decimal Amount,
    string Currency,
    string? PaymentUrl,
    string? QrString,
    DateTimeOffset? ExpiresAt,
    PaymentStatus PaymentStatus);

public sealed record InitiatePaymentResult(
    InitiatePaymentResponse? Payment,
    string? Error,
    bool IsConfigurationError = false)
{
    public static InitiatePaymentResult Success(InitiatePaymentResponse payment) =>
        new(payment, null);

    public static InitiatePaymentResult Failure(
        string error,
        bool isConfigurationError = false) =>
        new(null, error, isConfigurationError);
}

public sealed record ReconcilePaymentResponse(
    Guid OrderId,
    string OrderNumber,
    string Provider,
    string ProviderReference,
    PaymentWebhookStatus WebhookStatus,
    PaymentStatus PaymentStatus,
    OrderStatus OrderStatus);

public sealed record ReconcilePaymentResult(
    ReconcilePaymentResponse? Payment,
    string? Error,
    bool IsConfigurationError = false,
    bool IsNotFound = false)
{
    public static ReconcilePaymentResult Success(ReconcilePaymentResponse payment) =>
        new(payment, null);

    public static ReconcilePaymentResult Failure(
        string error,
        bool isConfigurationError = false,
        bool isNotFound = false) =>
        new(null, error, isConfigurationError, isNotFound);
}

public sealed class PaymentService(
    ZetruvDbContext db,
    PaymentGatewayResolver gatewayResolver,
    InventoryReservationService inventoryReservations)
{
    public async Task<InitiatePaymentResult> InitiateAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var gateway = gatewayResolver.Resolve();
        if (gateway is null)
        {
            return InitiatePaymentResult.Failure(
                "Payment provider is not configured.",
                isConfigurationError: true);
        }

        var order = await db.Orders
            .Include(x => x.Items)
            .Include(x => x.Transactions)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);

        if (order is null)
        {
            return InitiatePaymentResult.Failure("Order was not found.");
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return InitiatePaymentResult.Failure("Cancelled orders cannot be paid.");
        }

        if (order.PaymentStatus == PaymentStatus.Paid)
        {
            return InitiatePaymentResult.Failure("Order is already paid.");
        }

        if (order.PaymentStatus == PaymentStatus.Refunded)
        {
            return InitiatePaymentResult.Failure("Refunded orders cannot initiate a new payment.");
        }

        if (order.GrandTotal <= 0)
        {
            return InitiatePaymentResult.Failure("Order total must be greater than zero.");
        }

        var reservation = await inventoryReservations.ReserveAsync(order, cancellationToken);
        if (!reservation.IsSuccess)
        {
            return InitiatePaymentResult.Failure(
                reservation.Error ?? "Inventory could not be reserved.");
        }

        var gatewayResult = await gateway.CreatePaymentAsync(
            new PaymentGatewayCreateRequest(
                order.Id,
                order.OrderNumber,
                order.GrandTotal,
                order.Currency,
                order.CustomerName,
                order.CustomerEmail,
                order.CustomerPhone),
            cancellationToken);

        if (!gatewayResult.IsSuccess ||
            string.IsNullOrWhiteSpace(gatewayResult.ProviderReference))
        {
            await inventoryReservations.ReleaseAsync(order.Id, cancellationToken);
            return InitiatePaymentResult.Failure(
                gatewayResult.Error ?? "Payment provider failed to create a payment.");
        }

        var now = DateTimeOffset.UtcNow;
        var transaction = new PaymentTransaction
        {
            OrderId = order.Id,
            Provider = gateway.Name,
            ProviderReference = gatewayResult.ProviderReference,
            Type = PaymentTransactionType.Payment,
            Status = PaymentTransactionStatus.Pending,
            Amount = order.GrandTotal,
            Currency = order.Currency,
            CreatedAt = now,
            UpdatedAt = now
        };

        order.PaymentProvider = gateway.Name;
        order.PaymentReference = gatewayResult.ProviderReference;
        order.PaymentStatus = PaymentStatus.Pending;
        order.UpdatedAt = now;
        order.Transactions.Add(transaction);

        await db.SaveChangesAsync(cancellationToken);

        return InitiatePaymentResult.Success(
            new InitiatePaymentResponse(
                order.Id,
                order.OrderNumber,
                gateway.Name,
                gatewayResult.ProviderReference,
                order.GrandTotal,
                order.Currency,
                gatewayResult.PaymentUrl,
                gatewayResult.QrString,
                gatewayResult.ExpiresAt,
                order.PaymentStatus));
    }

    public async Task<ReconcilePaymentResult> ReconcileWebhookAsync(
        string provider,
        string rawBody,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken = default)
    {
        var gateway = gatewayResolver.ResolveByName(provider);
        if (gateway is null)
        {
            return ReconcilePaymentResult.Failure(
                "Payment provider is not supported.",
                isConfigurationError: true);
        }

        var parsed = await gateway.ParseWebhookAsync(rawBody, headers, cancellationToken);
        if (parsed.Notification is null)
        {
            return ReconcilePaymentResult.Failure(
                parsed.Error ?? "Webhook could not be verified.",
                parsed.IsConfigurationError);
        }

        var notification = parsed.Notification;
        var transaction = await db.PaymentTransactions
            .Include(x => x.Order)
            .SingleOrDefaultAsync(x =>
                x.Provider == gateway.Name &&
                x.ProviderReference == notification.ProviderReference &&
                x.Type == PaymentTransactionType.Payment,
                cancellationToken);

        if (transaction is null)
        {
            return ReconcilePaymentResult.Failure(
                "Payment transaction was not found.",
                isNotFound: true);
        }

        var order = transaction.Order;
        if (transaction.Amount != notification.Amount ||
            !string.Equals(
                transaction.Currency,
                notification.Currency,
                StringComparison.OrdinalIgnoreCase))
        {
            return ReconcilePaymentResult.Failure(
                "Webhook amount or currency does not match the payment transaction.");
        }

        var now = DateTimeOffset.UtcNow;

        switch (notification.Status)
        {
            case PaymentWebhookStatus.Pending:
                break;

            case PaymentWebhookStatus.Paid:
                if (order.PaymentStatus == PaymentStatus.Refunded)
                {
                    return ReconcilePaymentResult.Failure(
                        "A refunded order cannot transition back to paid.");
                }

                transaction.Status = PaymentTransactionStatus.Succeeded;
                transaction.ProcessedAt ??= now;
                transaction.UpdatedAt = now;
                order.PaymentStatus = PaymentStatus.Paid;
                order.PaidAt ??= now;
                if (order.Status == OrderStatus.Pending)
                {
                    order.Status = OrderStatus.Processing;
                }
                order.UpdatedAt = now;
                await db.SaveChangesAsync(cancellationToken);
                await inventoryReservations.ConsumeAsync(order.Id, cancellationToken);
                break;

            case PaymentWebhookStatus.Failed:
                if (transaction.Status != PaymentTransactionStatus.Succeeded)
                {
                    transaction.Status = PaymentTransactionStatus.Failed;
                    transaction.ProcessedAt ??= now;
                    transaction.UpdatedAt = now;
                }

                var hasSucceededPayment = await db.PaymentTransactions
                    .AnyAsync(x =>
                        x.OrderId == order.Id &&
                        x.Type == PaymentTransactionType.Payment &&
                        x.Status == PaymentTransactionStatus.Succeeded,
                        cancellationToken);

                var hasOtherPendingPayment = await db.PaymentTransactions
                    .AnyAsync(x =>
                        x.OrderId == order.Id &&
                        x.Id != transaction.Id &&
                        x.Type == PaymentTransactionType.Payment &&
                        x.Status == PaymentTransactionStatus.Pending,
                        cancellationToken);

                if (!hasSucceededPayment)
                {
                    order.PaymentStatus = PaymentStatus.Failed;
                    order.UpdatedAt = now;
                }

                await db.SaveChangesAsync(cancellationToken);
                if (!hasSucceededPayment && !hasOtherPendingPayment)
                {
                    await inventoryReservations.ReleaseAsync(order.Id, cancellationToken);
                }
                break;

            case PaymentWebhookStatus.Refunded:
                if (order.PaymentStatus != PaymentStatus.Paid &&
                    order.PaymentStatus != PaymentStatus.Refunded)
                {
                    return ReconcilePaymentResult.Failure(
                        "Only a paid order can be marked as refunded.");
                }

                order.PaymentStatus = PaymentStatus.Refunded;
                order.UpdatedAt = now;
                await db.SaveChangesAsync(cancellationToken);
                break;
        }

        return ReconcilePaymentResult.Success(
            new ReconcilePaymentResponse(
                order.Id,
                order.OrderNumber,
                gateway.Name,
                notification.ProviderReference,
                notification.Status,
                order.PaymentStatus,
                order.Status));
    }
}
