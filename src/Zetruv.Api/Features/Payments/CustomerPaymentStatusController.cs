using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Orders;
using Zetruv.Api.Features.Catalog;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Payments;

// A read-only projection of the existing payment state machine.
// GET is read-only. POST refresh delegates to verified reconciliation.
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
    DateTimeOffset CheckedAt)
{
    public string? GameItemSupportPhone { get; init; }
}

[ApiController]
[Route("api/v1/checkout/orders/{orderId:guid}/payment")]
public sealed class CustomerPaymentStatusController(
    ZetruvDbContext db,
    OrderAccessTokenService orderAccessTokens, PaymentService paymentService) : ControllerBase
{
    [HttpPost("refresh")]
    [EnableRateLimiting("payment-status-refresh")]
    public async Task<ActionResult<CustomerPaymentStatusResponse>> Refresh(
        Guid orderId, [FromHeader(Name = "X-Order-Access-Token")] string? orderAccessToken, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        if (!orderAccessTokens.Validate(orderId, orderAccessToken))
            return NotFound(new { message = "Order was not found or the access token is invalid." });
        var now = DateTimeOffset.UtcNow;
        var transactionId = await db.PaymentTransactions.AsNoTracking()
            .Where(x => x.OrderId == orderId && x.Type == PaymentTransactionType.Payment &&
                x.Status == PaymentTransactionStatus.Pending && x.Order.PaymentStatus == PaymentStatus.Pending &&
                x.ProviderReference != null && (!x.ExpiresAt.HasValue || x.ExpiresAt > now))
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        if (transactionId.HasValue)
        {
            var result = await paymentService.ReconcileTransactionAsync(transactionId.Value, ct, customerRefresh: true);
            if (result.Reconciliation is null && !result.IsConflict && !result.IsNotFound)
                return StatusCode(503, new { message = "Payment provider status is temporarily unavailable." });
        }
        return await Get(orderId, orderAccessToken, ct);
    }

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

        var state = PaymentSessionState.Resolve(
            order.PaymentStatus, order.Status,
            transactions.Select(x => new PaymentSessionSnapshot(
                x.Status, x.ExpiresAt, x.ProcessedAt, x.CreatedAt)), now);

        var credentialsUsable = order.Items.All(x =>
            x.FulfillmentMethod != FulfillmentMethod.MANUAL_LOGIN ||
            ManualLoginCredentialService.IsUsable(x.ManualLoginCredential, now));
        var canRetry =
            order.Status != OrderStatus.Cancelled &&
            (order.PaymentStatus is PaymentStatus.Pending or PaymentStatus.Failed) &&
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
            now)
        {
            GameItemSupportPhone = state == "Paid" && order.Items.Any(x => x.ProductKind == ProductKind.GameItem)
                ? await GameItemDelivery.GetSupportPhoneAsync(db, ct) : null
        });
    }
}
