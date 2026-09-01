using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Shipping;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Orders;

public sealed class OrderService(ZetruvDbContext db)
{
    public async Task<IReadOnlyList<RecentPurchaseResponse>> GetRecentPurchasesAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 50);

        return await db.OrderItems
            .AsNoTracking()
            .Where(x =>
                x.Order.Status == OrderStatus.Completed &&
                x.Order.PaymentStatus == PaymentStatus.Paid)
            .OrderByDescending(x => x.Order.CompletedAt ?? x.Order.PaidAt ?? x.Order.CreatedAt)
            .ThenByDescending(x => x.CreatedAt)
            .Take(limit)
            .Select(x => new RecentPurchaseResponse(
                x.Id,
                x.ProductId,
                x.ProductName,
                x.ProductSlug,
                x.ProductKind,
                x.VariantName,
                x.ThumbnailUrl,
                x.GameName,
                x.UnitPrice,
                x.Order.CompletedAt ?? x.Order.PaidAt ?? x.Order.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<OrderPageResponse> GetAdminOrdersAsync(
        OrderStatus? status,
        PaymentStatus? paymentStatus,
        string? query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var orders = db.Orders.AsNoTracking().AsQueryable();

        if (status.HasValue)
        {
            orders = orders.Where(x => x.Status == status.Value);
        }

        if (paymentStatus.HasValue)
        {
            orders = orders.Where(x => x.PaymentStatus == paymentStatus.Value);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            orders = orders.Where(x =>
                EF.Functions.ILike(x.OrderNumber, $"%{q}%") ||
                (x.CustomerName != null && EF.Functions.ILike(x.CustomerName, $"%{q}%")) ||
                (x.CustomerEmail != null && EF.Functions.ILike(x.CustomerEmail, $"%{q}%")) ||
                (x.CustomerPhone != null && EF.Functions.ILike(x.CustomerPhone, $"%{q}%")));
        }

        var totalItems = await orders.CountAsync(cancellationToken);
        var items = await orders
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new OrderListItemResponse(
                x.Id,
                x.OrderNumber,
                x.Status,
                x.PaymentStatus,
                x.CustomerName,
                x.GrandTotal,
                x.Currency,
                x.Items.Sum(i => i.Quantity),
                x.CreatedAt,
                x.PaidAt,
                x.CompletedAt))
            .ToListAsync(cancellationToken);

        return new OrderPageResponse(
            items,
            page,
            pageSize,
            totalItems,
            (int)Math.Ceiling(totalItems / (double)pageSize));
    }

    public async Task<OrderDetailResponse?> GetAdminOrderAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        await db.Orders
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new OrderDetailResponse(
                x.Id,
                x.OrderNumber,
                x.Status,
                x.PaymentStatus,
                x.CustomerName,
                x.CustomerEmail,
                x.CustomerPhone,
                x.Subtotal,
                x.DiscountAmount,
                x.ShippingAmount,
                x.GrandTotal,
                x.Currency,
                x.PaymentProvider,
                x.PaymentReference,
                x.PaidAt,
                x.CompletedAt,
                x.CreatedAt,
                x.UpdatedAt,
                x.Shipment == null
                    ? null
                    : new ShipmentAdminResponse(
                        x.Shipment.Status,
                        x.Shipment.Provider,
                        x.Shipment.ServiceCode,
                        x.Shipment.ServiceName,
                        x.Shipment.TrackingNumber,
                        x.Shipment.Cost,
                        x.Shipment.Currency,
                        x.Shipment.TotalWeightGrams,
                        x.Shipment.EtaMinDays,
                        x.Shipment.EtaMaxDays,
                        x.Shipment.RecipientName,
                        x.Shipment.Phone,
                        x.Shipment.AddressLine1,
                        x.Shipment.AddressLine2,
                        x.Shipment.District,
                        x.Shipment.City,
                        x.Shipment.Province,
                        x.Shipment.PostalCode,
                        x.Shipment.ShippedAt,
                        x.Shipment.DeliveredAt),
                x.Items
                    .OrderBy(i => i.CreatedAt)
                    .Select(i => new OrderItemResponse(
                        i.Id,
                        i.ProductId,
                        i.ProductVariantId,
                        i.ProductName,
                        i.ProductSlug,
                        i.ProductKind,
                        i.VariantName,
                        i.Sku,
                        i.ThumbnailUrl,
                        i.GameName,
                        i.UnitPrice,
                        i.Quantity,
                        i.LineTotal))
                    .ToList(),
                x.Transactions
                    .OrderByDescending(t => t.CreatedAt)
                    .Select(t => new PaymentTransactionResponse(
                        t.Id,
                        t.Provider,
                        t.ProviderReference,
                        t.Type,
                        t.Status,
                        t.Amount,
                        t.Currency,
                        t.ProcessedAt,
                        t.CreatedAt))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);
}
