using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.GameAccounts;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Orders;

public sealed class CheckoutService(ZetruvDbContext db)
{
    public async Task<CreateCheckoutOrderResult> CreateOrderAsync(
        CreateCheckoutOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var items = request.Items;
        if (items is null || items.Count == 0)
        {
            return CreateCheckoutOrderResult.Failure("At least one checkout item is required.");
        }

        if (string.IsNullOrWhiteSpace(request.CustomerEmail) &&
            string.IsNullOrWhiteSpace(request.CustomerPhone))
        {
            return CreateCheckoutOrderResult.Failure(
                "Customer email or phone is required.");
        }

        var groupedItems = items
            .GroupBy(x => new { x.ProductVariantId, x.GameAccountValidationId })
            .Select(x => new
            {
                x.Key.ProductVariantId,
                x.Key.GameAccountValidationId,
                Quantity = x.Sum(i => i.Quantity)
            })
            .ToList();

        if (groupedItems.Count > 50 || groupedItems.Any(x => x.Quantity is < 1 or > 99))
        {
            return CreateCheckoutOrderResult.Failure(
                "Checkout supports up to 50 distinct lines and 99 units per line.");
        }

        var duplicateValidation = groupedItems
            .Where(x => x.GameAccountValidationId.HasValue)
            .GroupBy(x => x.GameAccountValidationId!.Value)
            .Any(x => x.Count() > 1);

        if (duplicateValidation)
        {
            return CreateCheckoutOrderResult.Failure(
                "A game account validation can only be used for one checkout line.");
        }

        var variantIds = groupedItems
            .Select(x => x.ProductVariantId)
            .Distinct()
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
                ProductRequiresGameAccountValidation = x.Product.RequiresGameAccountValidation,
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

            if (variant.ProductRequiresGameAccountValidation &&
                !item.GameAccountValidationId.HasValue)
            {
                return CreateCheckoutOrderResult.Failure(
                    $"{variant.ProductName} requires game account validation before checkout.");
            }

            if (!variant.ProductRequiresGameAccountValidation &&
                item.GameAccountValidationId.HasValue)
            {
                return CreateCheckoutOrderResult.Failure(
                    $"{variant.ProductName} does not accept a game account validation.");
            }
        }

        var now = DateTimeOffset.UtcNow;
        var validationIds = groupedItems
            .Where(x => x.GameAccountValidationId.HasValue)
            .Select(x => x.GameAccountValidationId!.Value)
            .ToArray();

        var validations = validationIds.Length == 0
            ? []
            : await db.Set<GameAccountValidation>()
                .AsNoTracking()
                .Where(x => validationIds.Contains(x.Id))
                .Select(x => new
                {
                    x.Id,
                    x.ProductId,
                    x.OrderItemId,
                    x.ExpiresAt
                })
                .ToListAsync(cancellationToken);

        if (validations.Count != validationIds.Length)
        {
            return CreateCheckoutOrderResult.Failure(
                "One or more game account validations do not exist.");
        }

        var validationById = validations.ToDictionary(x => x.Id);
        foreach (var item in groupedItems.Where(x => x.GameAccountValidationId.HasValue))
        {
            var variant = variantById[item.ProductVariantId];
            var validation = validationById[item.GameAccountValidationId!.Value];

            if (validation.ProductId != variant.ProductId)
            {
                return CreateCheckoutOrderResult.Failure(
                    "Game account validation does not match the selected product.");
            }

            if (validation.OrderItemId.HasValue || validation.ExpiresAt <= now)
            {
                return CreateCheckoutOrderResult.Failure(
                    "Game account validation expired or was already used. Please validate again.");
            }
        }

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
        var validationClaims = new List<(Guid ValidationId, Guid OrderItemId)>();

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

            var orderItem = new OrderItem
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
            };

            orderItems.Add(orderItem);

            if (item.GameAccountValidationId.HasValue)
            {
                validationClaims.Add((item.GameAccountValidationId.Value, orderItem.Id));
            }

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

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var claim in validationClaims)
        {
            var affected = await db.Set<GameAccountValidation>()
                .Where(x =>
                    x.Id == claim.ValidationId &&
                    x.OrderItemId == null &&
                    x.ExpiresAt > now)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.OrderItemId, claim.OrderItemId)
                    .SetProperty(x => x.ConsumedAt, now),
                    cancellationToken);

            if (affected != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return CreateCheckoutOrderResult.Failure(
                    "Game account validation expired or was already used. Please validate again.");
            }
        }

        await transaction.CommitAsync(cancellationToken);

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
