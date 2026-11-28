using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Features.Catalog;
using Zetruv.Api.Features.GameAccounts;
using Zetruv.Api.Features.Shipping;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Orders;

public sealed record CustomerOrderTarget(
    string? AccountDisplayName,
    IReadOnlyDictionary<string, string> Fields);

public sealed record CustomerOrderDetailItem(
    Guid Id,
    string ProductName,
    string? GameName,
    string? VariantName,
    ProductKind Kind,
    FulfillmentStatus FulfillmentStatus,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    CustomerOrderTarget? Target)
{
    public string? ProductSlug { get; init; }
    public string? VariantIconUrl { get; init; }
    public string? RankFrom { get; init; }
    public string? RankTo { get; init; }
    public string? RankFromIconUrl { get; init; }
    public string? RankToIconUrl { get; init; }
    public string? ThumbnailUrl { get; init; }
    public bool CanReview { get; init; }
    public ProductReviewStatus? ReviewStatus { get; init; }
}

public sealed record CustomerOrderDetail(
    Guid Id,
    string OrderNumber,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal ShippingAmount,
    decimal GrandTotal,
    string Currency,
    string? VoucherCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<CustomerOrderDetailItem> Items)
{
    public string PaymentState { get; init; } = string.Empty;
    public InvoiceInfo? Invoice { get; init; }
    public bool HasFulfillmentIssue { get; init; }
    public ShipmentTrackingResponse? Shipment { get; init; }
    public string? GameItemSupportPhone { get; init; }
}

public enum CustomerOrderGroup { Active, History, Completed }

public sealed record CustomerOrderPreview(string ProductName, string? VariantName, string? ThumbnailUrl);

public sealed record CustomerOrderSummary(
    Guid Id, string OrderNumber, OrderStatus Status, string PaymentState,
    decimal GrandTotal, string Currency, DateTimeOffset CreatedAt,
    int ItemCount, bool HasFulfillmentIssue, CustomerOrderPreview? FirstItem);

public sealed record CustomerOrderPage(IReadOnlyList<CustomerOrderSummary> Items, bool HasMore);

[ApiController]
[Authorize(AuthenticationSchemes = CustomerAuthConstants.Scheme)]
[Route("api/v1/me/orders")]
public sealed class CustomerOrderDetailController(ZetruvDbContext db, InvoiceService invoices) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CustomerOrderPage>> List(
        [FromQuery] CustomerOrderGroup group = CustomerOrderGroup.Active,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        [FromQuery, Range(1, 50)] int limit = 20,
        CancellationToken ct = default)
    {
        Response.Headers.CacheControl = "private, no-store";
        if (!Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var customerId))
            return Unauthorized();

        var owned = db.Orders.AsNoTracking().Where(x => x.CustomerUserId == customerId);
        var orders = group switch
        {
            CustomerOrderGroup.Active => owned.Where(x =>
                x.Status != OrderStatus.Completed && x.Status != OrderStatus.Cancelled &&
                x.PaymentStatus != PaymentStatus.Refunded),
            CustomerOrderGroup.Completed => owned.Where(x =>
                x.Status == OrderStatus.Completed && x.PaymentStatus != PaymentStatus.Refunded),
            _ => owned.Where(x => x.Status == OrderStatus.Cancelled ||
                x.PaymentStatus == PaymentStatus.Refunded)
        };
        var now = DateTimeOffset.UtcNow;
        var page = await orders
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip(offset).Take(limit + 1)
            .Select(x => new
            {
                x.Id, x.OrderNumber, x.Status, x.PaymentStatus, x.GrandTotal, x.Currency, x.CreatedAt,
                ItemCount = x.Items.Sum(i => i.Quantity),
                HasFulfillmentIssue = x.Items.Any(i => i.FulfillmentStatus == FulfillmentStatus.Failed),
                FirstItem = x.Items.OrderBy(i => i.CreatedAt).ThenBy(i => i.Id)
                    .Select(i => new CustomerOrderPreview(i.ProductName, i.VariantName, i.ThumbnailUrl))
                    .FirstOrDefault(),
                HasActiveAttempt = x.Transactions.Any(t => t.Type == PaymentTransactionType.Payment &&
                    t.Status == PaymentTransactionStatus.Pending && (!t.ExpiresAt.HasValue || t.ExpiresAt > now)),
                LatestAttempt = x.Transactions.Where(t => t.Type == PaymentTransactionType.Payment)
                    .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
                    .Select(t => new PaymentSessionSnapshot(t.Status, t.ExpiresAt, t.ProcessedAt, t.CreatedAt))
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var items = page.Take(limit).Select(x => new CustomerOrderSummary(
            x.Id, x.OrderNumber, x.Status,
            PaymentSessionState.Resolve(x.PaymentStatus, x.Status, x.HasActiveAttempt, x.LatestAttempt, now),
            x.GrandTotal, x.Currency, x.CreatedAt, x.ItemCount, x.HasFulfillmentIssue, x.FirstItem))
            .ToList();
        return Ok(new CustomerOrderPage(items, page.Count > limit));
    }

    // Destination fields are copied from the account-validation contract,
    // never from encrypted MANUAL_LOGIN credentials. Defense in depth:
    // redact credential-looking keys if legacy rows contain unexpected data.
    private static readonly HashSet<string> UnsafeKeys = new(
        ["password", "pass", "passwd", "otp", "pin", "token", "secret",
         "credential", "credentials", "cookie", "session", "sessionid", "session_id"],
        StringComparer.OrdinalIgnoreCase);

    [HttpGet("{orderId:guid}")]
    public async Task<ActionResult<CustomerOrderDetail>> Get(Guid orderId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        if (!Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var customerId))
            return Unauthorized();

        var order = await db.Orders.AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Shipment)
            .SingleOrDefaultAsync(x => x.Id == orderId && x.CustomerUserId == customerId, ct);
        if (order is null)
            return NotFound(new { message = "Order was not found." });

        var itemIds = order.Items.Select(x => x.Id).ToArray();
        var reviewStatuses = await db.ProductReviews.AsNoTracking()
            .Where(x => x.CustomerUserId == customerId && itemIds.Contains(x.OrderItemId))
            .ToDictionaryAsync(x => x.OrderItemId, x => x.IsApproved ? ProductReviewStatus.Published :
                x.ReviewedAt == null ? ProductReviewStatus.Pending : ProductReviewStatus.Hidden, ct);
        var reviewEligible = order.Status == OrderStatus.Completed && order.PaymentStatus == PaymentStatus.Paid;
        var validations = await db.Set<GameAccountValidation>().AsNoTracking()
            .Where(x => x.OrderItemId.HasValue && itemIds.Contains(x.OrderItemId.Value))
            .Select(x => new { ItemId = x.OrderItemId!.Value, x.AccountDisplayName, x.InputJson })
            .ToListAsync(ct);

        var targets = validations.ToDictionary(
            x => x.ItemId,
            x => new CustomerOrderTarget(
                x.AccountDisplayName,
                SafeTargetFields(x.InputJson)));

        var items = order.Items
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(x => new CustomerOrderDetailItem(
                x.Id,
                x.ProductName,
                x.GameName,
                x.VariantName,
                x.ProductKind,
                x.FulfillmentStatus,
                x.Quantity,
                x.UnitPrice,
                x.LineTotal,
                targets.GetValueOrDefault(x.Id))
            {
                ProductSlug = x.ProductSlug,
                VariantIconUrl = x.VariantIconUrl,
                RankFrom = x.RankFrom,
                RankTo = x.RankTo,
                RankFromIconUrl = x.RankFromIconUrl,
                RankToIconUrl = x.RankToIconUrl,
                ThumbnailUrl = x.ThumbnailUrl,
                CanReview = reviewEligible && x.ProductId.HasValue && !reviewStatuses.ContainsKey(x.Id),
                ReviewStatus = reviewStatuses.TryGetValue(x.Id, out var status) ? status : null
            })
            .ToList();

        var attempts = await db.Set<PaymentTransaction>().AsNoTracking()
            .Where(t => t.OrderId == order.Id && t.Type == PaymentTransactionType.Payment)
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Select(t => new PaymentSessionSnapshot(t.Status, t.ExpiresAt, t.ProcessedAt, t.CreatedAt))
            .ToListAsync(ct);
        var shipment = order.Shipment;

        return Ok(new CustomerOrderDetail(
            order.Id, order.OrderNumber, order.Status, order.PaymentStatus,
            order.Subtotal, order.DiscountAmount, order.ShippingAmount,
            order.GrandTotal, order.Currency, order.VoucherCode,
            order.CreatedAt, order.PaidAt, order.CompletedAt, items)
        {
            Invoice = await invoices.Info(order.Id, ct),
            PaymentState = PaymentSessionState.Resolve(order.PaymentStatus, order.Status, attempts, DateTimeOffset.UtcNow),
            HasFulfillmentIssue = items.Any(x => x.FulfillmentStatus == FulfillmentStatus.Failed),
            GameItemSupportPhone = order.PaymentStatus == PaymentStatus.Paid && items.Any(x => x.Kind == ProductKind.GameItem)
                ? await GameItemDelivery.GetSupportPhoneAsync(db, ct) : null,
            Shipment = shipment is null ? null : new ShipmentTrackingResponse(
                shipment.Status, shipment.Provider, shipment.ServiceCode, shipment.ServiceName,
                shipment.TrackingNumber, shipment.EtaMinDays, shipment.EtaMaxDays,
                shipment.ShippedAt, shipment.DeliveredAt)
        });
    }

    private static IReadOnlyDictionary<string, string> SafeTargetFields(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>();

        try
        {
            var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return fields is null
                ? new Dictionary<string, string>()
                : fields
                    .Where(x => !UnsafeKeys.Contains(x.Key) &&
                                !string.IsNullOrWhiteSpace(x.Key) &&
                                !string.IsNullOrWhiteSpace(x.Value))
                    .ToDictionary(x => x.Key, x => x.Value);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }
}
