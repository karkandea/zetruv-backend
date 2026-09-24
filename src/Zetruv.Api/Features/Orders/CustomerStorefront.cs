using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Features.Catalog;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Orders;

public sealed class CustomerCartItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerUserId { get; set; }
    public Guid ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;
    public int Quantity { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record CartItemInput(
    [Required] Guid ProductVariantId,
    [Range(1, 99)] int Quantity);
public sealed record CartItemResponse(
    Guid Id, Guid ProductVariantId, Guid ProductId, string ProductName,
    string ProductSlug, string VariantName, ProductKind Kind,
    string? ThumbnailUrl, decimal UnitPrice, int Quantity, bool IsAvailable);
public sealed record CustomerCartResponse(IReadOnlyList<CartItemResponse> Items);

[ApiController]
[Authorize(AuthenticationSchemes = CustomerAuthConstants.Scheme)]
[Route("api/v1/me")]
public sealed class CustomerStorefrontController(ZetruvDbContext db) : ControllerBase
{
    private Guid CustomerId => Guid.Parse(
        User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [HttpGet("recent-purchases")]
    public async Task<IActionResult> RecentPurchases(
        [FromQuery] int limit = 5, CancellationToken ct = default)
    {
        var id = CustomerId;
        var purchases = await db.OrderItems.AsNoTracking()
            .Where(x => x.Order.CustomerUserId == id &&
                x.Order.Status == OrderStatus.Completed &&
                x.Order.PaymentStatus == PaymentStatus.Paid)
            .OrderByDescending(x => x.Order.CompletedAt ?? x.Order.PaidAt ?? x.Order.CreatedAt)
            .ThenByDescending(x => x.CreatedAt)
            .Take(Math.Clamp(limit, 1, 20))
            .Select(x => new RecentPurchaseResponse(
                x.Id, x.ProductId, x.ProductName, x.ProductSlug, x.ProductKind,
                x.FulfillmentMethod, x.FulfillmentStatus, x.VariantName,
                x.ThumbnailUrl, x.GameName, x.UnitPrice,
                x.Order.CompletedAt ?? x.Order.PaidAt ?? x.Order.CreatedAt))
            .ToListAsync(ct);
        Response.Headers.CacheControl = "private, no-store";
        return Ok(purchases);
    }

    [HttpGet("cart")]
    public async Task<IActionResult> GetCart(CancellationToken ct)
    {
        var id = CustomerId;
        var items = await db.Set<CustomerCartItem>().AsNoTracking()
            .Where(x => x.CustomerUserId == id)
            .OrderBy(x => x.UpdatedAt)
            .Select(x => new CartItemResponse(
                x.Id, x.ProductVariantId, x.ProductVariant.ProductId,
                x.ProductVariant.Product.Name, x.ProductVariant.Product.Slug,
                x.ProductVariant.Name, x.ProductVariant.Product.Kind,
                x.ProductVariant.Product.ThumbnailUrl, x.ProductVariant.Price,
                x.Quantity,
                x.ProductVariant.IsActive && x.ProductVariant.Product.IsActive &&
                x.ProductVariant.Product.Category.IsActive &&
                (x.ProductVariant.Product.Game == null || x.ProductVariant.Product.Game.IsActive) &&
                (!x.ProductVariant.StockQuantity.HasValue ||
                    x.ProductVariant.StockQuantity.Value >= x.Quantity) &&
                (x.ProductVariant.Product.Kind != ProductKind.GameAccount ||
                    (x.ProductVariant.StockQuantity == 1 && x.Quantity == 1)) &&
                (x.ProductVariant.Product.FulfillmentMethod == FulfillmentMethod.MANUAL ||
                 (x.ProductVariant.Product.FulfillmentMethod == FulfillmentMethod.AUTO_ID &&
                  x.ProductVariant.Product.InputFields.Any(f =>
                      f.Scope == ProductInputFieldScope.AccountValidation && f.IsRequired)) ||
                 (x.ProductVariant.Product.FulfillmentMethod == FulfillmentMethod.MANUAL_LOGIN &&
                  x.ProductVariant.Product.InputFields.Any(f =>
                      f.Scope == ProductInputFieldScope.LoginCredential && f.IsRequired)))))
            .ToListAsync(ct);
        var ids = items.Select(x => x.ProductVariantId).ToArray();
        var now = DateTimeOffset.UtcNow;
        var offers = await db.PromotionItems.AsNoTracking()
            .Where(x => ids.Contains(x.ProductVariantId) &&
                x.Promotion.IsActive && x.Promotion.IsFlashSale &&
                x.Promotion.StartsAt <= now && x.Promotion.EndsAt >= now &&
                x.SalePrice >= 0 && x.SalePrice <= x.ProductVariant.Price)
            .GroupBy(x => x.ProductVariantId)
            .Select(x => new { Id = x.Key, Price = x.Min(i => i.SalePrice) })
            .ToDictionaryAsync(x => x.Id, x => x.Price, ct);
        var effectiveItems = items.Select(x => offers.TryGetValue(x.ProductVariantId, out var price)
            ? x with { UnitPrice = price } : x).ToList();
        Response.Headers.CacheControl = "private, no-store";
        return Ok(new CustomerCartResponse(effectiveItems));
    }

    [HttpPut("cart/items/{variantId:guid}")]
    public async Task<IActionResult> UpsertCartItem(
        Guid variantId, CartItemInput request, CancellationToken ct)
    {
        if (variantId != request.ProductVariantId || variantId == Guid.Empty)
            return BadRequest(new { message = "Variant does not match request." });
        var variant = await db.ProductVariants.AsNoTracking()
            .Where(x => x.Id == variantId && x.IsActive && x.Product.IsActive &&
                x.Product.Category.IsActive &&
                (x.Product.Game == null || x.Product.Game.IsActive))
            .Select(x => new { x.StockQuantity, x.Product.Kind })
            .SingleOrDefaultAsync(ct);
        if (variant is null) return NotFound(new { message = "Product variant unavailable." });
        if (variant.StockQuantity.HasValue && request.Quantity > variant.StockQuantity.Value)
            return Conflict(new { message = "Requested quantity exceeds current stock." });
        if (variant.Kind == ProductKind.GameAccount &&
            (request.Quantity != 1 || variant.StockQuantity != 1))
            return BadRequest(new { message = "Game accounts can only be purchased one at a time." });

        var id = CustomerId;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({id.ToString()}))", ct);
        var item = await db.Set<CustomerCartItem>().SingleOrDefaultAsync(
            x => x.CustomerUserId == id && x.ProductVariantId == variantId, ct);
        if (item is null)
        {
            if (await db.Set<CustomerCartItem>().CountAsync(x => x.CustomerUserId == id, ct) >= 50)
                return Conflict(new { message = "Cart supports at most 50 distinct variants." });
            item = new CustomerCartItem { CustomerUserId = id, ProductVariantId = variantId };
            db.Set<CustomerCartItem>().Add(item);
        }
        item.Quantity = request.Quantity;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Ok(new { item.Id, item.ProductVariantId, item.Quantity });
    }

    [HttpDelete("cart/items/{variantId:guid}")]
    public async Task<IActionResult> RemoveCartItem(Guid variantId, CancellationToken ct)
    {
        await db.Set<CustomerCartItem>().Where(x =>
            x.CustomerUserId == CustomerId && x.ProductVariantId == variantId)
            .ExecuteDeleteAsync(ct);
        return NoContent();
    }

    [HttpDelete("cart")]
    public async Task<IActionResult> ClearCart(CancellationToken ct)
    {
        await db.Set<CustomerCartItem>().Where(x => x.CustomerUserId == CustomerId)
            .ExecuteDeleteAsync(ct);
        return NoContent();
    }
}
