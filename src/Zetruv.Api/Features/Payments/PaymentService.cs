using System.Data;
using System.Data.Common;
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
    PaymentStatus PaymentStatus,
    bool IsRecovery);

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
    bool IsNotFound = false,
    bool IsConflict = false)
{
    public static ReconcilePaymentResult Success(ReconcilePaymentResponse payment) =>
        new(payment, null);

    public static ReconcilePaymentResult Failure(
        string error,
        bool isConfigurationError = false,
        bool isNotFound = false,
        bool isConflict = false) =>
        new(null, error, isConfigurationError, isNotFound, isConflict);
}

public sealed class PaymentService(
    ZetruvDbContext db,
    PaymentGatewayResolver gatewayResolver,
    InventoryReservationService inventoryReservations,
    OrderFulfillmentService fulfillmentService,
    FulfillmentExecutionService executionService,
    ILogger<PaymentService> logger)
{
    public async Task<InitiatePaymentResult> InitiateAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
        }

        var (lockKey1, lockKey2) = PaymentLockKeys(orderId);
        await SetPaymentLockAsync(
            connection,
            acquire: true,
            lockKey1,
            lockKey2,
            cancellationToken);

        try
        {
            return await InitiateLockedAsync(orderId, cancellationToken);
        }
        finally
        {
            try
            {
                await SetPaymentLockAsync(
                    connection,
                    acquire: false,
                    lockKey1,
                    lockKey2,
                    CancellationToken.None);
            }
            finally
            {
                if (openedHere)
                {
                    await db.Database.CloseConnectionAsync();
                }
            }
        }
    }

    private async Task<InitiatePaymentResult> InitiateLockedAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
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

        var now = DateTimeOffset.UtcNow;
        var pendingPayments = order.Transactions
            .Where(x =>
                x.Type == PaymentTransactionType.Payment &&
                x.Status == PaymentTransactionStatus.Pending)
            .OrderByDescending(x => x.CreatedAt)
            .ToList();

        var expiredPayments = pendingPayments
            .Where(x => x.ExpiresAt.HasValue && x.ExpiresAt.Value <= now)
            .ToList();

        foreach (var expired in expiredPayments)
        {
            expired.Status = PaymentTransactionStatus.Failed;
            expired.ProcessedAt ??= now;
            expired.UpdatedAt = now;
        }

        var activePayment = pendingPayments.FirstOrDefault(x =>
            x.Status == PaymentTransactionStatus.Pending &&
            (!x.ExpiresAt.HasValue || x.ExpiresAt.Value > now));

        if (activePayment is not null)
        {
            order.PaymentProvider = activePayment.Provider;
            order.PaymentReference = activePayment.ProviderReference;
            order.PaymentStatus = PaymentStatus.Pending;
            order.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);

            return InitiatePaymentResult.Success(
                ToInitiateResponse(order, activePayment, isRecovery: true));
        }

        if (expiredPayments.Count > 0 && order.PaymentStatus == PaymentStatus.Pending)
        {
            order.PaymentStatus = PaymentStatus.Failed;
            order.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            await inventoryReservations.ReleaseAsync(order.Id, cancellationToken);
        }

        var gateway = gatewayResolver.Resolve();
        if (gateway is null)
        {
            return InitiatePaymentResult.Failure(
                "Payment provider is not configured.",
                isConfigurationError: true);
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

        if (gatewayResult.ExpiresAt.HasValue && gatewayResult.ExpiresAt.Value <= now)
        {
            await inventoryReservations.ReleaseAsync(order.Id, cancellationToken);
            return InitiatePaymentResult.Failure(
                "Payment provider returned an already expired payment session.");
        }

        var paymentTransaction = new PaymentTransaction
        {
            OrderId = order.Id,
            Provider = gateway.Name,
            ProviderReference = gatewayResult.ProviderReference.Trim(),
            Type = PaymentTransactionType.Payment,
            Status = PaymentTransactionStatus.Pending,
            Amount = order.GrandTotal,
            Currency = order.Currency,
            PaymentUrl = Clean(gatewayResult.PaymentUrl),
            QrString = Clean(gatewayResult.QrString, trim: false),
            ExpiresAt = gatewayResult.ExpiresAt,
            CreatedAt = now,
            UpdatedAt = now
        };

        order.PaymentProvider = gateway.Name;
        order.PaymentReference = paymentTransaction.ProviderReference;
        order.PaymentStatus = PaymentStatus.Pending;
        order.UpdatedAt = now;

        db.PaymentTransactions.Add(paymentTransaction);
        await db.SaveChangesAsync(cancellationToken);

        return InitiatePaymentResult.Success(
            ToInitiateResponse(order, paymentTransaction, isRecovery: false));
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
        var paymentTransaction = await db.PaymentTransactions
            .Include(x => x.Order)
                .ThenInclude(x => x.Items)
            .SingleOrDefaultAsync(x =>
                x.Provider == gateway.Name &&
                x.ProviderReference == notification.ProviderReference &&
                x.Type == PaymentTransactionType.Payment,
                cancellationToken);

        if (paymentTransaction is null)
        {
            return ReconcilePaymentResult.Failure(
                "Payment transaction was not found.",
                isNotFound: true);
        }

        var order = paymentTransaction.Order;
        if (paymentTransaction.Amount != notification.Amount ||
            !string.Equals(
                paymentTransaction.Currency,
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
                if (paymentTransaction.Status == PaymentTransactionStatus.Succeeded &&
                    order.PaymentStatus == PaymentStatus.Paid)
                {
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

                if (order.PaymentStatus == PaymentStatus.Refunded)
                {
                    return ReconcilePaymentResult.Failure(
                        "A refunded order cannot transition back to paid.");
                }

                if (order.Status == OrderStatus.Cancelled)
                {
                    return ReconcilePaymentResult.Failure(
                        "A cancelled order cannot transition to paid.",
                        isConflict: true);
                }

                if (order.PaymentStatus == PaymentStatus.Paid &&
                    paymentTransaction.Status != PaymentTransactionStatus.Succeeded)
                {
                    return ReconcilePaymentResult.Failure(
                        "Order is already paid by another payment transaction. Manual reconciliation is required.",
                        isConflict: true);
                }

                await using (var dbTransaction = await db.Database.BeginTransactionAsync(cancellationToken))
                {
                    var inventory = await inventoryReservations.EnsureConsumedForPaidAsync(
                        order,
                        cancellationToken);

                    if (!inventory.IsSuccess)
                    {
                        await dbTransaction.RollbackAsync(cancellationToken);
                        return ReconcilePaymentResult.Failure(
                            inventory.Error ?? "Inventory could not be secured for the paid order.",
                            isConflict: true);
                    }

                    paymentTransaction.Status = PaymentTransactionStatus.Succeeded;
                    paymentTransaction.ProcessedAt ??= now;
                    paymentTransaction.UpdatedAt = now;
                    order.PaymentStatus = PaymentStatus.Paid;
                    order.PaymentProvider = paymentTransaction.Provider;
                    order.PaymentReference = paymentTransaction.ProviderReference;
                    order.PaidAt ??= now;
                    fulfillmentService.StartPaidOrder(order, now);

                    await db.PaymentTransactions
                        .Where(x =>
                            x.OrderId == order.Id &&
                            x.Id != paymentTransaction.Id &&
                            x.Type == PaymentTransactionType.Payment &&
                            x.Status == PaymentTransactionStatus.Pending)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(x => x.Status, PaymentTransactionStatus.Failed)
                            .SetProperty(x => x.ProcessedAt, now)
                            .SetProperty(x => x.UpdatedAt, now),
                            cancellationToken);

                    await db.SaveChangesAsync(cancellationToken);
                    await dbTransaction.CommitAsync(cancellationToken);
                }

                try
                {
                    await executionService.ExecuteAutoItemsForOrderAsync(
                        order.Id,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "Automatic fulfillment dispatch failed after payment for order {OrderId}.",
                        order.Id);
                }
                break;

            case PaymentWebhookStatus.Failed:
                if (paymentTransaction.Status != PaymentTransactionStatus.Succeeded)
                {
                    paymentTransaction.Status = PaymentTransactionStatus.Failed;
                    paymentTransaction.ProcessedAt ??= now;
                    paymentTransaction.UpdatedAt = now;
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
                        x.Id != paymentTransaction.Id &&
                        x.Type == PaymentTransactionType.Payment &&
                        x.Status == PaymentTransactionStatus.Pending &&
                        (!x.ExpiresAt.HasValue || x.ExpiresAt > now),
                        cancellationToken);

                if (!hasSucceededPayment)
                {
                    order.PaymentStatus = hasOtherPendingPayment
                        ? PaymentStatus.Pending
                        : PaymentStatus.Failed;
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

    private static InitiatePaymentResponse ToInitiateResponse(
        Order order,
        PaymentTransaction transaction,
        bool isRecovery) =>
        new(
            order.Id,
            order.OrderNumber,
            transaction.Provider,
            transaction.ProviderReference ?? string.Empty,
            transaction.Amount,
            transaction.Currency,
            transaction.PaymentUrl,
            transaction.QrString,
            transaction.ExpiresAt,
            order.PaymentStatus,
            isRecovery);

    private static (int Key1, int Key2) PaymentLockKeys(Guid orderId)
    {
        var bytes = orderId.ToByteArray();
        return (
            BitConverter.ToInt32(bytes, 0) ^ BitConverter.ToInt32(bytes, 8),
            BitConverter.ToInt32(bytes, 4) ^ BitConverter.ToInt32(bytes, 12));
    }

    private static async Task SetPaymentLockAsync(
        DbConnection connection,
        bool acquire,
        int key1,
        int key2,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = acquire
            ? "SELECT pg_advisory_lock(@key1, @key2)"
            : "SELECT pg_advisory_unlock(@key1, @key2)";

        var first = command.CreateParameter();
        first.ParameterName = "@key1";
        first.Value = key1;
        command.Parameters.Add(first);

        var second = command.CreateParameter();
        second.ParameterName = "@key2";
        second.Value = key2;
        command.Parameters.Add(second);

        await command.ExecuteScalarAsync(cancellationToken);
    }

    private static string? Clean(string? value, bool trim = true)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return trim ? value.Trim() : value;
    }
}
