using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Features.Catalog;
using Zetruv.Api.Features.GameAccounts;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Orders;

public sealed class CustomerCartItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerUserId { get; set; }
    public Guid ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;
    public Guid? GameAccountValidationId { get; set; }
    [MaxLength(80)] public string LineKey { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record CartItemInput(
    [Required] Guid ProductVariantId,
    [Range(1, 99)] int Quantity,
    Guid? GameAccountValidationId = null);

public sealed record CartTargetResponse(
    Guid ValidationId,
    string? AccountDisplayName,
    IReadOnlyDictionary<string, string> Fields,
    DateTimeOffset ExpiresAt);

public sealed record CartItemResponse(
    Guid Id, Guid ProductVariantId, Guid ProductId, string ProductName,
    string ProductSlug, string VariantName, ProductKind Kind,
    string? ThumbnailUrl, decimal UnitPrice, int Quantity, bool IsAvailable)
{
    public CartTargetResponse? Target { get; init; }
}

public sealed record CustomerCartResponse(IReadOnlyList<CartItemResponse> Items);

// Null means destination fields were edited and the old verification
// must no longer make this particular cart line checkout-eligible.
public sealed record ChangeCartLineTargetRequest(Guid? GameAccountValidationId);

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
        var rawItems = await db.CustomerCartItems.AsNoTracking()
            .Where(x => x.CustomerUserId == id)
            .OrderBy(x => x.UpdatedAt)
            .Select(x => new
            {
                x.Id,
                x.ProductVariantId,
                x.ProductVariant.ProductId,
                ProductName = x.ProductVariant.Product.Name,
                ProductSlug = x.ProductVariant.Product.Slug,
                VariantName = x.ProductVariant.Name,
                Kind = x.ProductVariant.Product.Kind,
                ThumbnailUrl = x.ProductVariant.Product.ThumbnailUrl,
                UnitPrice = x.ProductVariant.Price,
                x.Quantity,
                x.GameAccountValidationId,
                FulfillmentMethod = x.ProductVariant.Product.FulfillmentMethod,
                RequiresValidation = x.ProductVariant.Product.RequiresGameAccountValidation,
                BaseAvailable =
                    x.ProductVariant.IsActive &&
                    x.ProductVariant.Product.IsActive &&
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
                          f.Scope == ProductInputFieldScope.LoginCredential && f.IsRequired)))
            })
            .ToListAsync(ct);

        var validationIds = rawItems
            .Where(x => x.GameAccountValidationId.HasValue)
            .Select(x => x.GameAccountValidationId!.Value)
            .Distinct()
            .ToArray();
        var validations = await db.Set<GameAccountValidation>().AsNoTracking()
            .Where(x => validationIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.ProductId,
                x.AccountDisplayName,
                x.InputJson,
                x.ExpiresAt,
                x.OrderItemId,
                x.ConsumedAt
            })
            .ToDictionaryAsync(x => x.Id, ct);

        var ids = rawItems.Select(x => x.ProductVariantId).Distinct().ToArray();
        var now = DateTimeOffset.UtcNow;
        var offers = await db.PromotionItems.AsNoTracking()
            .Where(x => ids.Contains(x.ProductVariantId) &&
                x.Promotion.IsActive && x.Promotion.IsFlashSale &&
                x.Promotion.StartsAt <= now && x.Promotion.EndsAt >= now &&
                x.SalePrice >= 0 && x.SalePrice <= x.ProductVariant.Price)
            .GroupBy(x => x.ProductVariantId)
            .Select(x => new { Id = x.Key, Price = x.Min(i => i.SalePrice) })
            .ToDictionaryAsync(x => x.Id, x => x.Price, ct);

        var items = rawItems.Select(x =>
        {
            CartTargetResponse? target = null;
            var targetRequired =
                x.FulfillmentMethod == FulfillmentMethod.AUTO_ID &&
                x.RequiresValidation;
            var targetAvailable = !targetRequired && !x.GameAccountValidationId.HasValue;

            if (x.GameAccountValidationId.HasValue &&
                validations.TryGetValue(x.GameAccountValidationId.Value, out var validation))
            {
                target = new CartTargetResponse(
                    validation.Id,
                    validation.AccountDisplayName,
                    ParseTargetFields(validation.InputJson),
                    validation.ExpiresAt);
                targetAvailable =
                    targetRequired &&
                    validation.ProductId == x.ProductId &&
                    validation.OrderItemId is null &&
                    validation.ConsumedAt is null &&
                    validation.ExpiresAt > now;
            }

            return new CartItemResponse(
                x.Id,
                x.ProductVariantId,
                x.ProductId,
                x.ProductName,
                x.ProductSlug,
                x.VariantName,
                x.Kind,
                x.ThumbnailUrl,
                offers.TryGetValue(x.ProductVariantId, out var offer)
                    ? offer
                    : x.UnitPrice,
                x.Quantity,
                x.BaseAvailable && targetAvailable)
            {
                Target = target
            };
        }).ToList();

        Response.Headers.CacheControl = "private, no-store";
        return Ok(new CustomerCartResponse(items));
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
            .Select(x => new
            {
                x.ProductId,
                x.StockQuantity,
                x.Product.Kind,
                x.Product.FulfillmentMethod,
                x.Product.RequiresGameAccountValidation
            })
            .SingleOrDefaultAsync(ct);
        if (variant is null)
            return NotFound(new { message = "Product variant unavailable." });
        if (variant.StockQuantity.HasValue &&
            request.Quantity > variant.StockQuantity.Value)
            return Conflict(new { message = "Requested quantity exceeds current stock." });
        if (variant.Kind == ProductKind.GameAccount &&
            (request.Quantity != 1 || variant.StockQuantity != 1))
            return BadRequest(new { message = "Game accounts can only be purchased one at a time." });

        var targetRequired =
            variant.FulfillmentMethod == FulfillmentMethod.AUTO_ID &&
            variant.RequiresGameAccountValidation;
        if (targetRequired && !request.GameAccountValidationId.HasValue)
            return BadRequest(new { message = "A verified account target is required for this cart line." });
        if (!targetRequired && request.GameAccountValidationId.HasValue)
            return BadRequest(new { message = "This product does not accept an account target in cart." });

        if (request.GameAccountValidationId.HasValue)
        {
            var now = DateTimeOffset.UtcNow;
            var validTarget = await db.Set<GameAccountValidation>().AsNoTracking()
                .AnyAsync(x =>
                    x.Id == request.GameAccountValidationId.Value &&
                    x.ProductId == variant.ProductId &&
                    x.OrderItemId == null &&
                    x.ConsumedAt == null &&
                    x.ExpiresAt > now,
                    ct);
            if (!validTarget)
                return Conflict(new { message = "Account target is expired, consumed, or does not match this product. Validate it again." });
        }

        var id = CustomerId;
        var lineKey = BuildLineKey(variantId, request.GameAccountValidationId);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({id.ToString()}))", ct);

        var item = await db.CustomerCartItems.SingleOrDefaultAsync(
            x => x.CustomerUserId == id && x.LineKey == lineKey, ct);
        if (item is null)
        {
            if (await db.CustomerCartItems.CountAsync(
                x => x.CustomerUserId == id, ct) >= 50)
                return Conflict(new { message = "Cart supports at most 50 distinct lines." });
            item = new CustomerCartItem
            {
                CustomerUserId = id,
                ProductVariantId = variantId,
                GameAccountValidationId = request.GameAccountValidationId,
                LineKey = lineKey
            };
            db.CustomerCartItems.Add(item);
        }

        item.Quantity = request.Quantity;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Ok(new
        {
            item.Id,
            item.ProductVariantId,
            item.GameAccountValidationId,
            item.Quantity
        });
    }

    // Figma: editing User ID / Zone on ONE cart item must invalidate
    // that item's checkout eligibility without deleting its sibling SKUs.
    [HttpPatch("cart/lines/{cartItemId:guid}/target")]
    public async Task<IActionResult> ChangeCartLineTarget(
        Guid cartItemId, ChangeCartLineTargetRequest request, CancellationToken ct)
    {
        var id = CustomerId;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({id.ToString()}))", ct);

        var line = await db.CustomerCartItems
            .Include(x => x.ProductVariant)
                .ThenInclude(x => x.Product)
            .SingleOrDefaultAsync(
                x => x.CustomerUserId == id && x.Id == cartItemId, ct);
        if (line is null)
            return NotFound(new { message = "Cart line was not found." });

        var product = line.ProductVariant.Product;
        if (product.FulfillmentMethod != FulfillmentMethod.AUTO_ID ||
            !product.RequiresGameAccountValidation)
            return BadRequest(new { message = "Only validated AUTO_ID cart items can change their account target." });

        var targetId = request.GameAccountValidationId;
        if (targetId.HasValue)
        {
            var now = DateTimeOffset.UtcNow;
            var isVerified = await db.Set<GameAccountValidation>()
                .AsNoTracking()
                .AnyAsync(x =>
                    x.Id == targetId.Value &&
                    x.ProductId == product.Id &&
                    x.OrderItemId == null &&
                    x.ConsumedAt == null &&
                    x.ExpiresAt > now, ct);
            if (!isVerified)
                return Conflict(new
                {
                    message = "The replacement account has not been verified, has expired, or belongs to another product. Validate the new User ID and Zone first."
                });
        }

        // Keep a unique identity for every edited, unverified line of the
        // same SKU; :default is reserved for products not requiring a target.
        var nextKey = targetId.HasValue
            ? BuildLineKey(line.ProductVariantId, targetId)
            : $"{line.ProductVariantId:N}:reverify:{line.Id:N}";

        if (await db.CustomerCartItems.AnyAsync(x =>
            x.CustomerUserId == id && x.Id != line.Id && x.LineKey == nextKey, ct))
            return Conflict(new
            {
                message = "This product and verified destination already exist on another cart line."
            });

        line.GameAccountValidationId = targetId;
        line.LineKey = nextKey;
        line.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Ok(new
        {
            line.Id,
            line.ProductVariantId,
            line.GameAccountValidationId,
            line.Quantity,
            NeedsReverification = !targetId.HasValue
        });
    }

    [HttpDelete("cart/lines/{cartItemId:guid}")]
    public async Task<IActionResult> RemoveCartLine(
        Guid cartItemId,
        CancellationToken ct)
    {
        await db.CustomerCartItems.Where(x =>
            x.CustomerUserId == CustomerId && x.Id == cartItemId)
            .ExecuteDeleteAsync(ct);
        return NoContent();
    }

    // Backward-compatible variant delete. When the same SKU has multiple
    // account targets, this intentionally removes every line for that variant.
    [HttpDelete("cart/items/{variantId:guid}")]
    public async Task<IActionResult> RemoveCartItem(
        Guid variantId,
        CancellationToken ct)
    {
        await db.CustomerCartItems.Where(x =>
            x.CustomerUserId == CustomerId &&
            x.ProductVariantId == variantId)
            .ExecuteDeleteAsync(ct);
        return NoContent();
    }

    [HttpDelete("cart")]
    public async Task<IActionResult> ClearCart(CancellationToken ct)
    {
        await db.CustomerCartItems
            .Where(x => x.CustomerUserId == CustomerId)
            .ExecuteDeleteAsync(ct);
        return NoContent();
    }

    private static string BuildLineKey(
        Guid variantId,
        Guid? validationId) =>
        validationId.HasValue
            ? $"{variantId:N}:{validationId.Value:N}"
            : $"{variantId:N}:default";

    private static IReadOnlyDictionary<string, string> ParseTargetFields(
        string? inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson))
            return new Dictionary<string, string>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(inputJson)
                ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }
}
