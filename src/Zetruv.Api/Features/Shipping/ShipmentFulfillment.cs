using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Features.Orders;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Shipping;

public sealed record UpdateShipmentRequest(
    ShipmentStatus Status,
    [MaxLength(180)] string? TrackingNumber = null);

public sealed record ShipmentFulfillmentResult(
    ShipmentAdminResponse? Shipment,
    string? Error,
    bool NotFound = false)
{
    public static ShipmentFulfillmentResult Success(ShipmentAdminResponse shipment) =>
        new(shipment, null);

    public static ShipmentFulfillmentResult Missing() =>
        new(null, null, true);

    public static ShipmentFulfillmentResult Failure(string error) =>
        new(null, error);
}

public sealed class ShipmentFulfillmentService(ZetruvDbContext db)
{
    public async Task<ShipmentFulfillmentResult> UpdateAsync(
        Guid orderId,
        UpdateShipmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var order = await db.Orders
            .Include(x => x.Shipment)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);

        if (order?.Shipment is null)
        {
            return ShipmentFulfillmentResult.Missing();
        }

        var shipment = order.Shipment;
        var now = DateTimeOffset.UtcNow;
        var trackingNumber = Clean(request.TrackingNumber) ?? shipment.TrackingNumber;

        if (order.Status == OrderStatus.Cancelled && request.Status != ShipmentStatus.Cancelled)
        {
            return ShipmentFulfillmentResult.Failure(
                "A cancelled order cannot move to an active shipment status.");
        }

        if (!IsAllowedTransition(shipment.Status, request.Status))
        {
            return ShipmentFulfillmentResult.Failure(
                $"Shipment cannot transition from {shipment.Status} to {request.Status}.");
        }

        if (request.Status is ShipmentStatus.Shipped or ShipmentStatus.Delivered &&
            string.IsNullOrWhiteSpace(trackingNumber))
        {
            return ShipmentFulfillmentResult.Failure(
                "Tracking number is required when a shipment is shipped or delivered.");
        }

        shipment.Status = request.Status;
        shipment.TrackingNumber = trackingNumber;
        shipment.UpdatedAt = now;

        switch (request.Status)
        {
            case ShipmentStatus.Pending:
            case ShipmentStatus.ReadyToShip:
                shipment.ShippedAt = null;
                shipment.DeliveredAt = null;
                break;

            case ShipmentStatus.Shipped:
                shipment.ShippedAt ??= now;
                shipment.DeliveredAt = null;
                break;

            case ShipmentStatus.Delivered:
                shipment.ShippedAt ??= now;
                shipment.DeliveredAt ??= now;
                break;

            case ShipmentStatus.Cancelled:
                shipment.DeliveredAt = null;
                break;
        }

        await db.SaveChangesAsync(cancellationToken);
        return ShipmentFulfillmentResult.Success(ToResponse(shipment));
    }

    public async Task CancelUnshippedAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var shipment = await db.Set<Shipment>()
            .SingleOrDefaultAsync(x => x.OrderId == orderId, cancellationToken);

        if (shipment is null ||
            shipment.Status is ShipmentStatus.Shipped or ShipmentStatus.Delivered or ShipmentStatus.Cancelled)
        {
            return;
        }

        shipment.Status = ShipmentStatus.Cancelled;
        shipment.UpdatedAt = DateTimeOffset.UtcNow;
        shipment.DeliveredAt = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static bool IsAllowedTransition(ShipmentStatus current, ShipmentStatus next)
    {
        if (current == next)
        {
            return true;
        }

        return current switch
        {
            ShipmentStatus.Pending => next is ShipmentStatus.ReadyToShip or ShipmentStatus.Cancelled,
            ShipmentStatus.ReadyToShip => next is ShipmentStatus.Shipped or ShipmentStatus.Cancelled,
            ShipmentStatus.Shipped => next == ShipmentStatus.Delivered,
            ShipmentStatus.Delivered => false,
            ShipmentStatus.Cancelled => false,
            _ => false
        };
    }

    private static ShipmentAdminResponse ToResponse(Shipment shipment) =>
        new(
            shipment.Status,
            shipment.Provider,
            shipment.ServiceCode,
            shipment.ServiceName,
            shipment.TrackingNumber,
            shipment.Cost,
            shipment.Currency,
            shipment.TotalWeightGrams,
            shipment.EtaMinDays,
            shipment.EtaMaxDays,
            shipment.RecipientName,
            shipment.Phone,
            shipment.AddressLine1,
            shipment.AddressLine2,
            shipment.District,
            shipment.City,
            shipment.Province,
            shipment.PostalCode,
            shipment.ShippedAt,
            shipment.DeliveredAt);

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

[ApiController]
[Authorize(Policy = AuthPolicies.CmsAdmin)]
[Route("api/v1/cms/orders/{orderId:guid}/shipment")]
public sealed class CmsShipmentController(
    ShipmentFulfillmentService fulfillmentService) : ControllerBase
{
    [HttpPut]
    public async Task<ActionResult<ShipmentAdminResponse>> Update(
        Guid orderId,
        UpdateShipmentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await fulfillmentService.UpdateAsync(orderId, request, cancellationToken);
        if (result.NotFound)
        {
            return NotFound(new { message = "Shipment was not found for this order." });
        }

        if (result.Shipment is null)
        {
            return BadRequest(new { message = result.Error });
        }

        return Ok(result.Shipment);
    }
}
