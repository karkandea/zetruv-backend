using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Features.Shipping;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Orders;

[ApiController]
[Authorize(Policy = AuthPolicies.CmsAdmin)]
[Route("api/v1/cms/orders")]
public sealed class CmsOrdersController(
    ZetruvDbContext db,
    OrderService orderService,
    InventoryReservationService inventoryReservations,
    ShipmentFulfillmentService shipmentFulfillment) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<OrderPageResponse>> GetOrders(
        [FromQuery] OrderStatus? status,
        [FromQuery] PaymentStatus? paymentStatus,
        [FromQuery(Name = "q")] string? query,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await orderService.GetAdminOrdersAsync(
            status,
            paymentStatus,
            query,
            page,
            pageSize,
            cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDetailResponse>> GetOrder(
        Guid id,
        CancellationToken cancellationToken)
    {
        var order = await orderService.GetAdminOrderAsync(id, cancellationToken);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        UpdateOrderStatusRequest request,
        CancellationToken cancellationToken)
    {
        var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        order.Status = request.Status;
        order.UpdatedAt = DateTimeOffset.UtcNow;

        if (request.Status == OrderStatus.Completed)
        {
            order.CompletedAt ??= DateTimeOffset.UtcNow;
        }
        else
        {
            order.CompletedAt = null;
        }

        await db.SaveChangesAsync(cancellationToken);

        if (request.Status == OrderStatus.Cancelled)
        {
            await inventoryReservations.ReleaseAsync(id, cancellationToken);
            await shipmentFulfillment.CancelUnshippedAsync(id, cancellationToken);
        }

        return NoContent();
    }

    [HttpPut("{id:guid}/payment-status")]
    public async Task<IActionResult> UpdatePaymentStatus(
        Guid id,
        UpdatePaymentStatusRequest request,
        CancellationToken cancellationToken)
    {
        var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        order.PaymentStatus = request.Status;
        order.UpdatedAt = DateTimeOffset.UtcNow;

        if (request.Status == PaymentStatus.Paid)
        {
            order.PaidAt ??= DateTimeOffset.UtcNow;
            if (order.Status == OrderStatus.Pending)
            {
                order.Status = OrderStatus.Processing;
            }
        }
        else if (request.Status is PaymentStatus.Pending or PaymentStatus.Failed)
        {
            order.PaidAt = null;
        }

        await db.SaveChangesAsync(cancellationToken);

        if (request.Status == PaymentStatus.Paid)
        {
            await inventoryReservations.ConsumeAsync(id, cancellationToken);
        }
        else if (request.Status == PaymentStatus.Failed)
        {
            await inventoryReservations.ReleaseAsync(id, cancellationToken);
        }

        return NoContent();
    }
}
