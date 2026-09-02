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

public sealed class PaymentService(
    ZetruvDbContext db,
    PaymentGatewayResolver gatewayResolver)
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
}
