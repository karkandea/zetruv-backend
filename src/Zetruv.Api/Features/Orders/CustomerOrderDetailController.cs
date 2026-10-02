using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Features.Catalog;
using Zetruv.Api.Features.GameAccounts;
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
    CustomerOrderTarget? Target);

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
    IReadOnlyList<CustomerOrderDetailItem> Items);

[ApiController]
[Authorize(AuthenticationSchemes = CustomerAuthConstants.Scheme)]
[Route("api/v1/me/orders")]
public sealed class CustomerOrderDetailController(ZetruvDbContext db) : ControllerBase
{
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
            .SingleOrDefaultAsync(x => x.Id == orderId && x.CustomerUserId == customerId, ct);
        if (order is null)
            return NotFound(new { message = "Order was not found." });

        var itemIds = order.Items.Select(x => x.Id).ToArray();
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
                targets.GetValueOrDefault(x.Id)))
            .ToList();

        return Ok(new CustomerOrderDetail(
            order.Id, order.OrderNumber, order.Status, order.PaymentStatus,
            order.Subtotal, order.DiscountAmount, order.ShippingAmount,
            order.GrandTotal, order.Currency, order.VoucherCode,
            order.CreatedAt, order.PaidAt, order.CompletedAt, items));
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
