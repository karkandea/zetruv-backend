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
    OrderFulfillmentService fulfillmentService,
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
        var order = await db.Orders
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (request.Status == order.Status)
        {
            return NoContent();
        }

        if (request.Status != OrderStatus.Cancelled)
        {
            return BadRequest(new
            {
                message = "Order status is derived from payment and per-item fulfillment. Update item fulfillment instead."
            });
        }

        if (order.Items.Any(x => x.FulfillmentStatus == FulfillmentStatus.Completed))
        {
            return Conflict(new
            {
                message = "An order with completed fulfillment items cannot be cancelled."
            });
        }

        var alreadyShipped = await db.Set<Shipment>()
            .AsNoTracking()
            .AnyAsync(
                x => x.OrderId == id &&
                     (x.Status == ShipmentStatus.Shipped ||
                      x.Status == ShipmentStatus.Delivered),
                cancellationToken);

        if (alreadyShipped)
        {
            return Conflict(new
            {
                message = "A shipped or delivered merchandise order cannot be cancelled."
            });
        }

        fulfillmentService.CancelOrder(order, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(cancellationToken);

        await inventoryReservations.ReleaseAsync(id, cancellationToken);
        await shipmentFulfillment.CancelUnshippedAsync(id, cancellationToken);

        return NoContent();
    }

    [HttpPut("{id:guid}/payment-status")]
    public async Task<IActionResult> UpdatePaymentStatus(
        Guid id,
        UpdatePaymentStatusRequest request,
        CancellationToken cancellationToken)
    {
        var order = await db.Orders
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        if (order.PaymentStatus == PaymentStatus.Paid &&
            request.Status is PaymentStatus.Pending or PaymentStatus.Failed)
        {
            return Conflict(new
            {
                message = "A paid order cannot move back to pending or failed payment status."
            });
        }

        if (request.Status == PaymentStatus.Paid)
        {
            if (order.Status == OrderStatus.Cancelled)
            {
                return Conflict(new { message = "A cancelled order cannot transition to paid." });
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var inventory = await inventoryReservations.EnsureConsumedForPaidAsync(
                order,
                cancellationToken);

            if (!inventory.IsSuccess)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Conflict(new
                {
                    message = inventory.Error ?? "Inventory could not be secured for the paid order."
                });
            }

            var now = DateTimeOffset.UtcNow;
            order.PaymentStatus = PaymentStatus.Paid;
            order.PaidAt ??= now;
            fulfillmentService.StartPaidOrder(order, now);

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return NoContent();
        }

        order.PaymentStatus = request.Status;
        order.UpdatedAt = DateTimeOffset.UtcNow;

        if (request.Status is PaymentStatus.Pending or PaymentStatus.Failed)
        {
            order.PaidAt = null;
        }

        await db.SaveChangesAsync(cancellationToken);

        if (request.Status == PaymentStatus.Failed)
        {
            await inventoryReservations.ReleaseAsync(id, cancellationToken);
        }

        return NoContent();
    }

    [HttpPut("{orderId:guid}/items/{orderItemId:guid}/fulfillment")]
    public async Task<ActionResult<OrderItemFulfillmentResponse>> UpdateItemFulfillment(
        Guid orderId,
        Guid orderItemId,
        UpdateOrderItemFulfillmentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await fulfillmentService.UpdateItemAsync(
            orderId,
            orderItemId,
            request,
            cancellationToken);

        if (result.Fulfillment is not null)
        {
            return Ok(result.Fulfillment);
        }

        if (result.NotFound)
        {
            return NotFound();
        }

        if (result.Conflict)
        {
            return Conflict(new { message = result.Error });
        }

        return BadRequest(new { message = result.Error });
    }

}
