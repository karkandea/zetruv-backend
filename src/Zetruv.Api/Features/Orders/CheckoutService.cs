using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Orders;

public sealed class CheckoutService(ZetruvDbContext db)
{
    public async Task<CreateCheckoutOrderResult> CreateOrderAsync(
        CreateCheckoutOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if ((request.Items?.Count ?? 0) == 0)
        {
            return CreateCheckoutOrderResult.Failure("At least one checkout item is required.");
        }

        if (string.IsNullOrWhiteSpace(request.CustomerEmail) &&
            string.IsNullOrWhiteSpace(request.CustomerPhone))
        {
            return CreateCheckoutOrderResult.Failure(
                "Customer email or phone is required.");
        }

        var groupedItems = request.Items
            .GroupBy(x => x.ProductVariantId)
            .Select(x => new
            {
                ProductVariantId = x.Key,
                Quantity = x.Sum(i => i.Quantity)
            })
            .ToList();

        if (groupedItems.Count > 50 || groupedItems.Any(x => x.Quantity is < 1 or > 99))
        {
            return CreateCheckoutOrderResult.Failure(
                "Checkout supports up to 50 distinct variants and 99 units per variant.");
        }

        var variantIds = groupedItems
            .Select(x => x.ProductVariantId)
            .ToArray();

        var variants = await db.ProductVariants
            .AsNoTracking()
            .Where(x => variantIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Sku,
                x.Price,
                x.StockQuantity,
                x.IsActive,
                ProductId = x.Product.Id,
                ProductName = x.Product.Name,
                ProductSlug = x.Product.Slug,
                ProductKind = x.Product.Kind,
                ProductThumbnailUrl = x.Product.ThumbnailUrl,
                ProductIsActive = x.Product.IsActive,
                CategoryIsActive = x.Product.Category.IsActive,
                GameName = x.Product.Game == null ? null : x.Product.Game.Name,
                GameIsActive = x.Product.Game == null || x.Product.Game.IsActive
            })
            .ToListAsync(cancellationToken);

        if (variants.Count != variantIds.Length)
        {
            return CreateCheckoutOrderResult.Failure(
                "One or more product variants do not exist.");
        }

        var variantById = variants.ToDictionary(x => x.Id);
        foreach (var item in groupedItems)
        {
            var variant = variantById[item.ProductVariantId];
            if (!variant.IsActive || !variant.ProductIsActive ||
                !variant.CategoryIsActive || !variant.GameIsActive)
            {
                return CreateCheckoutOrderResult.Failure(
                    $"{variant.ProductName} / {variant.Name} is not available for checkout.");
            }

            if (variant.StockQuantity.HasValue &&
                item.Quantity > variant.StockQuantity.Value)
            {
                return CreateCheckoutOrderResult.Failure(
                    $"Insufficient stock for {variant.ProductName} / {variant.Name}.");
            }
        }

        var now = DateTimeOffset.UtcNow;
        var salePrices = await db.PromotionItems
            .AsNoTracking()
            .Where(x =>
                variantIds.Contains(x.ProductVariantId) &&
                x.Promotion.IsActive &&
                x.Promotion.IsFlashSale &&
                x.Promotion.StartsAt <= now &&
                x.Promotion.EndsAt >= now)
            .GroupBy(x => x.ProductVariantId)
            .Select(x => new
            {
                ProductVariantId = x.Key,
                SalePrice = x.Min(i => i.SalePrice)
            })
            .ToDictionaryAsync(
                x => x.ProductVariantId,
                x => x.SalePrice,
                cancellationToken);

        decimal subtotal = 0;
        decimal discountAmount = 0;
        var orderItems = new List<OrderItem>(groupedItems.Count);
        var responseItems = new List<CheckoutOrderItemResponse>(groupedItems.Count);

        foreach (var item in groupedItems)
        {
            var variant = variantById[item.ProductVariantId];
            var regularUnitPrice = variant.Price;
            var unitPrice = regularUnitPrice;

            if (salePrices.TryGetValue(variant.Id, out var salePrice) &&
                salePrice >= 0 && salePrice <= regularUnitPrice)
            {
                unitPrice = salePrice;
            }

            var regularLineTotal = regularUnitPrice * item.Quantity;
            var lineTotal = unitPrice * item.Quantity;
            subtotal += regularLineTotal;
            discountAmount += regularLineTotal - lineTotal;

            orderItems.Add(new OrderItem
            {
                ProductId = variant.ProductId,
                ProductVariantId = variant.Id,
                ProductName = variant.ProductName,
                ProductSlug = variant.ProductSlug,
                ProductKind = variant.ProductKind,
                VariantName = variant.Name,
                Sku = variant.Sku,
                ThumbnailUrl = variant.ProductThumbnailUrl,
                GameName = variant.GameName,
                UnitPrice = unitPrice,
                Quantity = item.Quantity,
                LineTotal = lineTotal,
                CreatedAt = now
            });

            responseItems.Add(new CheckoutOrderItemResponse(
                variant.Id,
                variant.ProductName,
                variant.ProductSlug,
                variant.ProductKind,
                variant.Name,
                variant.ProductThumbnailUrl,
                variant.GameName,
                unitPrice,
                item.Quantity,
                lineTotal));
        }

        const decimal shippingAmount = 0;
        var grandTotal = subtotal - discountAmount + shippingAmount;

        var order = new Order
        {
            OrderNumber = CreateOrderNumber(now),
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            CustomerName = Clean(request.CustomerName),
            CustomerEmail = Clean(request.CustomerEmail),
            CustomerPhone = Clean(request.CustomerPhone),
            Subtotal = subtotal,
            DiscountAmount = discountAmount,
            ShippingAmount = shippingAmount,
            GrandTotal = grandTotal,
            Currency = "IDR",
            CreatedAt = now,
            UpdatedAt = now,
            Items = orderItems
        };

        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        return CreateCheckoutOrderResult.Success(
            new CreateCheckoutOrderResponse(
                order.Id,
                order.OrderNumber,
                order.Status,
                order.PaymentStatus,
                order.Subtotal,
                order.DiscountAmount,
                order.ShippingAmount,
                order.GrandTotal,
                order.Currency,
                responseItems,
                order.CreatedAt));
    }

    private static string CreateOrderNumber(DateTimeOffset now)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        return $"ZTR-{now:yyyyMMdd}-{suffix}";
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
