using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Orders;

public sealed record OrderItemFulfillmentResult(
    OrderItemFulfillmentResponse? Fulfillment,
    string? Error,
    bool NotFound = false,
    bool Conflict = false)
{
    public static OrderItemFulfillmentResult Success(OrderItemFulfillmentResponse fulfillment) =>
        new(fulfillment, null);

    public static OrderItemFulfillmentResult Missing() =>
        new(null, null, NotFound: true);

    public static OrderItemFulfillmentResult Failure(string error, bool conflict = false) =>
        new(null, error, Conflict: conflict);
}

public sealed class OrderFulfillmentService(ZetruvDbContext db)
{
    public void StartPaidOrder(Order order, DateTimeOffset now)
    {
        foreach (var item in order.Items.Where(x => x.FulfillmentStatus == FulfillmentStatus.Pending))
        {
            item.FulfillmentStatus = FulfillmentStatus.Processing;
            item.FulfillmentStartedAt ??= now;
            item.FulfilledAt = null;
            item.FulfillmentMessage = null;
        }

        RecalculateOrder(order, now);
    }

    public void CancelOrder(Order order, DateTimeOffset now)
    {
        foreach (var item in order.Items.Where(x =>
                     x.FulfillmentStatus is not FulfillmentStatus.Completed and
                     not FulfillmentStatus.Cancelled))
        {
            item.FulfillmentStatus = FulfillmentStatus.Cancelled;
            item.FulfilledAt = null;
        }

        order.Status = OrderStatus.Cancelled;
        order.CompletedAt = null;
        order.UpdatedAt = now;
    }

    public async Task<OrderItemFulfillmentResult> UpdateItemAsync(
        Guid orderId,
        Guid orderItemId,
        UpdateOrderItemFulfillmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var order = await db.Orders
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);

        if (order is null)
        {
            return OrderItemFulfillmentResult.Missing();
        }

        var item = order.Items.SingleOrDefault(x => x.Id == orderItemId);
        if (item is null)
        {
            return OrderItemFulfillmentResult.Missing();
        }

        if (request.Status is FulfillmentStatus.Pending or FulfillmentStatus.Cancelled)
        {
            return OrderItemFulfillmentResult.Failure(
                "Pending and Cancelled are system-managed fulfillment states.");
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return OrderItemFulfillmentResult.Failure(
                "Cancelled orders cannot resume fulfillment.",
                conflict: true);
        }

        if (order.PaymentStatus != PaymentStatus.Paid)
        {
            return OrderItemFulfillmentResult.Failure(
                "Order must be paid before fulfillment can be updated.",
                conflict: true);
        }

        if (!IsAllowedTransition(item.FulfillmentStatus, request.Status))
        {
            return OrderItemFulfillmentResult.Failure(
                $"Fulfillment cannot transition from {item.FulfillmentStatus} to {request.Status}.",
                conflict: true);
        }

        var now = DateTimeOffset.UtcNow;
        item.FulfillmentStatus = request.Status;
        item.FulfillmentReference = Clean(request.Reference) ?? item.FulfillmentReference;
        item.FulfillmentMessage = Clean(request.Message);

        switch (request.Status)
        {
            case FulfillmentStatus.Processing:
                item.FulfillmentStartedAt ??= now;
                item.FulfilledAt = null;
                break;

            case FulfillmentStatus.Completed:
                item.FulfillmentStartedAt ??= now;
                item.FulfilledAt ??= now;
                item.FulfillmentMessage = null;
                break;

            case FulfillmentStatus.Failed:
                item.FulfillmentStartedAt ??= now;
                item.FulfilledAt = null;
                break;
        }

        RecalculateOrder(order, now);
        await db.SaveChangesAsync(cancellationToken);

        return OrderItemFulfillmentResult.Success(
            new OrderItemFulfillmentResponse(
                order.Id,
                item.Id,
                item.FulfillmentMethod,
                item.FulfillmentStatus,
                item.FulfillmentReference,
                item.FulfillmentMessage,
                item.FulfillmentStartedAt,
                item.FulfilledAt,
                order.Status,
                order.Items.Any(x => x.FulfillmentStatus == FulfillmentStatus.Failed)));
    }

    public async Task SyncMerchandiseFromShipmentAsync(
        Guid orderId,
        FulfillmentStatus status,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var order = await db.Orders
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);

        if (order is null)
        {
            return;
        }

        foreach (var item in order.Items.Where(x => x.ProductKind == Catalog.ProductKind.Merchandise))
        {
            if (item.FulfillmentStatus is FulfillmentStatus.Completed or FulfillmentStatus.Cancelled)
            {
                continue;
            }

            item.FulfillmentStatus = status;
            if (status == FulfillmentStatus.Processing)
            {
                item.FulfillmentStartedAt ??= now;
                item.FulfilledAt = null;
            }
            else if (status == FulfillmentStatus.Completed)
            {
                item.FulfillmentStartedAt ??= order.PaidAt ?? now;
                item.FulfilledAt ??= now;
                item.FulfillmentMessage = null;
            }
            else if (status == FulfillmentStatus.Cancelled)
            {
                item.FulfilledAt = null;
            }
        }

        RecalculateOrder(order, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    public void RecalculateOrder(Order order, DateTimeOffset now)
    {
        if (order.Status == OrderStatus.Cancelled)
        {
            return;
        }

        if (order.PaymentStatus != PaymentStatus.Paid)
        {
            order.Status = OrderStatus.Pending;
            order.CompletedAt = null;
            order.UpdatedAt = now;
            return;
        }

        var items = order.Items.ToList();
        if (items.Count > 0 && items.All(x => x.FulfillmentStatus == FulfillmentStatus.Completed))
        {
            order.Status = OrderStatus.Completed;
            order.CompletedAt ??= now;
        }
        else
        {
            order.Status = OrderStatus.Processing;
            order.CompletedAt = null;
        }

        order.UpdatedAt = now;
    }

    private static bool IsAllowedTransition(FulfillmentStatus current, FulfillmentStatus next)
    {
        if (current == next)
        {
            return true;
        }

        return current switch
        {
            FulfillmentStatus.Pending => next == FulfillmentStatus.Processing,
            FulfillmentStatus.Processing =>
                next is FulfillmentStatus.Completed or FulfillmentStatus.Failed,
            FulfillmentStatus.Failed => next == FulfillmentStatus.Processing,
            FulfillmentStatus.Completed => false,
            FulfillmentStatus.Cancelled => false,
            _ => false
        };
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
