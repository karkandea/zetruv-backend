using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Orders;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Payments;

// A read-only projection of the existing payment state machine.
// This endpoint NEVER settles payments; only verified provider callbacks or
// reconciliation are allowed to make a payment Paid.
public sealed record CustomerPaymentStatusResponse(
    Guid OrderId,
    string OrderNumber,
    PaymentStatus PaymentStatus,
    string State,
    bool CanRetry,
    bool HasActivePaymentSession,
    string? Provider,
    string? ProviderReference,
    string? PaymentUrl,
    string? QrString,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset CheckedAt);

[ApiController]
[Route("api/v1/checkout/orders/{orderId:guid}/payment")]
public sealed class CustomerPaymentStatusController(
    ZetruvDbContext db,
    OrderAccessTokenService orderAccessTokens) : ControllerBase
{
    [HttpGet]
    [EnableRateLimiting("payment-status")]
    public async Task<ActionResult<CustomerPaymentStatusResponse>> Get(
        Guid orderId,
        [FromHeader(Name = "X-Order-Access-Token")] string? orderAccessToken,
        CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        if (!orderAccessTokens.Validate(orderId, orderAccessToken))
            return NotFound(new { message = "Order was not found or the access token is invalid." });

        var order = await db.Orders
            .AsNoTracking()
            .Include(x => x.Transactions)
            .Include(x => x.Items)
                .ThenInclude(x => x.ManualLoginCredential)
            .SingleOrDefaultAsync(x => x.Id == orderId, ct);

        if (order is null)
            return NotFound(new { message = "Order was not found or the access token is invalid." });

        var now = DateTimeOffset.UtcNow;
        var transactions = order.Transactions
            .Where(x => x.Type == PaymentTransactionType.Payment)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .ToList();

        var latest = transactions.FirstOrDefault();
        var active = transactions.FirstOrDefault(x =>
            x.Status == PaymentTransactionStatus.Pending &&
            (!x.ExpiresAt.HasValue || x.ExpiresAt.Value > now));

        // A failed provider attempt is Failed, even if it is read after its
        // original deadline. Expired means the payment window elapsed before
        // settlement, including an attempt released by payment initiation.
        var failedBeforeExpiry = latest?.Status == PaymentTransactionStatus.Failed &&
            (!latest.ExpiresAt.HasValue ||
             (latest.ProcessedAt.HasValue && latest.ProcessedAt < latest.ExpiresAt));

        var state = order.PaymentStatus switch
        {
            PaymentStatus.Paid => "Paid",
            PaymentStatus.Refunded => "Refunded",
            _ when order.Status == OrderStatus.Cancelled => "Cancelled",
            _ when active is not null => "Pending",
            _ when latest is null && order.PaymentStatus == PaymentStatus.Failed => "Failed",
            _ when latest is null => "NotStarted",
            _ when failedBeforeExpiry => "Failed",
            _ when latest.ExpiresAt.HasValue && latest.ExpiresAt.Value <= now => "Expired",
            _ when latest.Status == PaymentTransactionStatus.Failed => "Failed",
            _ => "Pending"
        };

        var credentialsUsable = order.Items.All(x =>
            x.FulfillmentMethod != Catalog.FulfillmentMethod.MANUAL_LOGIN ||
            ManualLoginCredentialService.IsUsable(x.ManualLoginCredential, now));
        var canRetry =
            order.Status != OrderStatus.Cancelled &&
            order.PaymentStatus is PaymentStatus.Pending or PaymentStatus.Failed &&
            active is null &&
            credentialsUsable;

        return Ok(new CustomerPaymentStatusResponse(
            order.Id,
            order.OrderNumber,
            order.PaymentStatus,
            state,
            canRetry,
            active is not null,
            active?.Provider ?? latest?.Provider,
            active?.ProviderReference ?? latest?.ProviderReference,
            active?.PaymentUrl,
            active?.QrString,
            active?.ExpiresAt ?? latest?.ExpiresAt,
            order.PaidAt,
            now));
    }
}
