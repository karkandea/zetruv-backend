using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Catalog;
using Zetruv.Api.Features.Shipping;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Orders;

public sealed record TrackOrderRequest(
    [Required, MaxLength(40)] string OrderNumber,
    [EmailAddress, MaxLength(320)] string? CustomerEmail,
    [MaxLength(50)] string? CustomerPhone);

public sealed record TrackOrderItemResponse(
    string ProductName,
    string ProductSlug,
    ProductKind ProductKind,
    string? VariantName,
    string? ThumbnailUrl,
    string? GameName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);

public sealed record TrackOrderResponse(
    string OrderNumber,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal ShippingAmount,
    decimal GrandTotal,
    string Currency,
    bool CanInitiatePayment,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset? CompletedAt,
    ShipmentTrackingResponse? Shipment,
    IReadOnlyList<TrackOrderItemResponse> Items);

public sealed class OrderTrackingService(ZetruvDbContext db)
{
    public async Task<TrackOrderResponse?> TrackAsync(
        TrackOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var orderNumber = Clean(request.OrderNumber);
        var email = Clean(request.CustomerEmail);
        var phone = Clean(request.CustomerPhone);

        if (string.IsNullOrWhiteSpace(orderNumber) ||
            (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(phone)))
        {
            return null;
        }

        return await db.Orders
            .AsNoTracking()
            .Where(x =>
                EF.Functions.ILike(x.OrderNumber, orderNumber) &&
                ((email != null && x.CustomerEmail != null && EF.Functions.ILike(x.CustomerEmail, email)) ||
                 (phone != null && x.CustomerPhone != null && x.CustomerPhone == phone)))
            .Select(x => new TrackOrderResponse(
                x.OrderNumber,
                x.Status,
                x.PaymentStatus,
                x.Subtotal,
                x.DiscountAmount,
                x.ShippingAmount,
                x.GrandTotal,
                x.Currency,
                x.Status != OrderStatus.Cancelled &&
                    (x.PaymentStatus == PaymentStatus.Pending || x.PaymentStatus == PaymentStatus.Failed),
                x.CreatedAt,
                x.PaidAt,
                x.CompletedAt,
                x.Shipment == null
                    ? null
                    : new ShipmentTrackingResponse(
                        x.Shipment.Status,
                        x.Shipment.Provider,
                        x.Shipment.ServiceCode,
                        x.Shipment.ServiceName,
                        x.Shipment.TrackingNumber,
                        x.Shipment.EtaMinDays,
                        x.Shipment.EtaMaxDays,
                        x.Shipment.ShippedAt,
                        x.Shipment.DeliveredAt),
                x.Items
                    .OrderBy(i => i.CreatedAt)
                    .Select(i => new TrackOrderItemResponse(
                        i.ProductName,
                        i.ProductSlug,
                        i.ProductKind,
                        i.VariantName,
                        i.ThumbnailUrl,
                        i.GameName,
                        i.UnitPrice,
                        i.Quantity,
                        i.LineTotal))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

[ApiController]
[Route("api/v1/orders")]
public sealed class OrderTrackingController(OrderTrackingService trackingService) : ControllerBase
{
    [HttpPost("lookup")]
    [EnableRateLimiting("order-lookup")]
    public async Task<ActionResult<TrackOrderResponse>> Lookup(
        TrackOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerEmail) &&
            string.IsNullOrWhiteSpace(request.CustomerPhone))
        {
            return BadRequest(new { message = "Customer email or phone is required." });
        }

        var order = await trackingService.TrackAsync(request, cancellationToken);
        if (order is null)
        {
            return NotFound(new { message = "Order was not found for the supplied details." });
        }

        return Ok(order);
    }
}
