using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Orders;

public sealed record CustomerInvoiceLine(
    Guid OrderItemId,
    string ProductName,
    string? VariantName,
    string? Sku,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record CustomerOrderInvoiceResponse(
    Guid OrderId,
    string InvoiceNumber,
    DateTimeOffset IssuedAt,
    string? CustomerName,
    string? CustomerEmail,
    PaymentStatus PaymentStatus,
    DateTimeOffset? PaidAt,
    string Currency,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal ShippingAmount,
    decimal GrandTotal,
    string? VoucherCode,
    IReadOnlyList<CustomerInvoiceLine> Items);

[ApiController]
[Authorize(AuthenticationSchemes = CustomerAuthConstants.Scheme)]
[Route("api/v1/me/orders")]
public sealed class CustomerOrderInvoiceController(ZetruvDbContext db) : ControllerBase
{
    // Invoice data is available from order creation, even while payment is
    // Pending. The customer UI may render/download it; this is NOT a PDF API.
    [HttpGet("{orderId:guid}/invoice")]
    public async Task<ActionResult<CustomerOrderInvoiceResponse>> Get(
        Guid orderId,
        CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        if (!Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var ownerId))
            return Unauthorized();

        var order = await db.Orders.AsNoTracking()
            .Include(x => x.Items)
            .SingleOrDefaultAsync(
                x => x.Id == orderId && x.CustomerUserId == ownerId, ct);
        if (order is null)
            return NotFound(new { message = "Order was not found." });

        var lines = order.Items
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Select(x => new CustomerInvoiceLine(
                x.Id, x.ProductName, x.VariantName, x.Sku,
                x.Quantity, x.UnitPrice, x.LineTotal))
            .ToList();

        return Ok(new CustomerOrderInvoiceResponse(
            order.Id,
            order.OrderNumber,
            order.CreatedAt,
            order.CustomerName,
            order.CustomerEmail,
            order.PaymentStatus,
            order.PaidAt,
            order.Currency,
            order.Subtotal,
            order.DiscountAmount,
            order.ShippingAmount,
            order.GrandTotal,
            order.VoucherCode,
            lines));
    }
}
