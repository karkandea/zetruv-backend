using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
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
    public FulfillmentMethod FulfillmentMethod { get; init; }
    public decimal RegularPrice { get; init; }
    public int? StockQuantity { get; init; }
    public bool RequiresGameAccountValidation { get; init; }
    public string? GameName { get; init; }
}

public sealed record CustomerCartResponse(IReadOnlyList<CartItemResponse> Items)
{
    public string Revision { get; init; } = string.Empty;
}

// Contains no raw account credentials; target IDs are issued by the account-validator API.
public sealed record CustomerCartBatchRequest(
    [Required] IReadOnlyList<CartItemInput> Items,
    string? Revision = null);

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
                x.ProductVariant.StockQuantity,
                GameName = x.ProductVariant.Product.Game != null ? x.ProductVariant.Product.Game.Name : null,
                x.Quantity,
                x.GameAccountValidationId,
                x.UpdatedAt,
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
                Target = target,
                FulfillmentMethod = x.FulfillmentMethod,
                RegularPrice = x.UnitPrice,
                StockQuantity = x.StockQuantity,
                RequiresGameAccountValidation = x.RequiresValidation,
                GameName = x.GameName
            };
        }).ToList();

        Response.Headers.CacheControl = "private, no-store";
        return Ok(new CustomerCartResponse(items)
        {
            Revision = ComputeCartRevision(rawItems.Select(x => (
                x.Id, x.ProductVariantId, x.GameAccountValidationId, x.Quantity, x.UpdatedAt)))
        });
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


    /// <summary>
    /// Complete authenticated cart snapshot. Used for single-device edits and
    /// remote-device convergence. Invalid or expired target IDs fail CLOSED.
    /// Only non-sensitive validated account targets may be persisted.
    /// </summary>
    [HttpPut("cart")]
    public Task<IActionResult> ReplaceCart(
        CustomerCartBatchRequest request, CancellationToken ct) =>
        ApplyCartBatchAsync(request, merge: false, ct);

    /// <summary>
    /// Atomically merge a guest cart with the owner's existing cart on login.
    /// Same logical SKU+destination uses MAX quantity, never SUM, to avoid
    /// multiplying purchases after repeat login or concurrent browser replay.
    /// </summary>
    [HttpPost("cart/merge")]
    public Task<IActionResult> MergeGuestCart(
        CustomerCartBatchRequest request, CancellationToken ct) =>
        ApplyCartBatchAsync(request, merge: true, ct);

    private async Task<IActionResult> ApplyCartBatchAsync(
        CustomerCartBatchRequest request, bool merge, CancellationToken ct)
    {
        if (request?.Items is null || request.Items.Count > 50)
            return BadRequest(new { message = "Cart must contain no more than 50 lines." });

        var customerId = CustomerId;
        var now = DateTimeOffset.UtcNow;
        var incomingIds = request.Items.Select(x => x.ProductVariantId).Distinct().ToArray();
        if (request.Items.Any(x => x.ProductVariantId == Guid.Empty ||
            x.Quantity < 1 || x.Quantity > 99))
            return BadRequest(new { message = "Cart contains an invalid SKU or quantity." });

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({customerId.ToString()}))", ct);

        var existing = await db.CustomerCartItems
            .Where(x => x.CustomerUserId == customerId).ToListAsync(ct);
        var currentRevision = ComputeCartRevision(existing.Select(x => (
            x.Id, x.ProductVariantId, x.GameAccountValidationId, x.Quantity, x.UpdatedAt)));
        if (!merge && !string.Equals(request.Revision, currentRevision, StringComparison.Ordinal))
            return Conflict(new { code = "CART_CHANGED",
                message = "Cart changed in another session. Reload and review before retrying." });
        var variants = await db.ProductVariants.AsNoTracking()
            .Where(x => incomingIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.ProductId,
                x.StockQuantity,
                x.IsActive,
                x.Product.Kind,
                x.Product.FulfillmentMethod,
                x.Product.RequiresGameAccountValidation,
                ProductActive = x.Product.IsActive && x.Product.Category.IsActive &&
                    (x.Product.Game == null || x.Product.Game.IsActive)
            }).ToDictionaryAsync(x => x.Id, ct);

        var validationIds = existing.Where(x => x.GameAccountValidationId.HasValue)
            .Select(x => x.GameAccountValidationId!.Value)
            .Concat(request.Items.Where(x => x.GameAccountValidationId.HasValue)
                .Select(x => x.GameAccountValidationId!.Value))
            .Distinct().ToArray();
        var validations = await db.Set<GameAccountValidation>().AsNoTracking()
            .Where(x => validationIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        // Build the canonical target identity from validator fingerprint, not
        // from raw client supplied user ID or a newly issued validation UUID.
        string Identity(Guid variantId, Guid? validationId)
        {
            if (!validationId.HasValue) return $"{variantId:N}:default";
            if (!validations.TryGetValue(validationId.Value, out var verified) ||
                string.IsNullOrWhiteSpace(verified.InputFingerprint))
                return $"{variantId:N}:{validationId.Value:N}";
            return $"{variantId:N}:{verified.InputFingerprint}";
        }

        var next = new Dictionary<string, CustomerCartItem>(StringComparer.Ordinal);
        if (merge)
            foreach (var old in existing)
            {
                var identity = Identity(old.ProductVariantId, old.GameAccountValidationId);
                if (!next.ContainsKey(identity)) next[identity] = old;
            }

        foreach (var entry in request.Items)
        {
            if (!variants.TryGetValue(entry.ProductVariantId, out var variant) ||
                !variant.IsActive || !variant.ProductActive ||
                (variant.StockQuantity.HasValue && variant.StockQuantity.Value < entry.Quantity) ||
                (variant.Kind == ProductKind.GameAccount &&
                    (entry.Quantity != 1 || variant.StockQuantity != 1)))
                return Conflict(new { message = "Cart SKU is unavailable or stock has changed." });

            var requiresTarget = variant.FulfillmentMethod == FulfillmentMethod.AUTO_ID &&
                variant.RequiresGameAccountValidation;
            if (requiresTarget != entry.GameAccountValidationId.HasValue)
                return BadRequest(new { message = "Cart target must match the product validation policy." });
            if (entry.GameAccountValidationId.HasValue)
            {
                if (!validations.TryGetValue(entry.GameAccountValidationId.Value, out var verified) ||
                    verified.ProductId != variant.ProductId || verified.ExpiresAt <= now ||
                    verified.ConsumedAt.HasValue || verified.OrderItemId.HasValue)
                    return Conflict(new { message = "Account target has expired or is not valid for this SKU." });
            }

            var identity = Identity(entry.ProductVariantId, entry.GameAccountValidationId);
            if (next.TryGetValue(identity, out var oldRow))
            {
                // Repeat import is idempotent; preserve existing line and use
                // higher quantity instead of blindly incrementing it.
                oldRow.Quantity = Math.Max(oldRow.Quantity, entry.Quantity);
                if (entry.GameAccountValidationId.HasValue &&
                    oldRow.GameAccountValidationId != entry.GameAccountValidationId)
                {
                    // A previous same-destination capability may have expired,
                    // been consumed, or been attached to an order. Replace ONLY
                    // that unusable target with the newly verified capability.
                    var priorUsable = oldRow.GameAccountValidationId.HasValue &&
                        validations.TryGetValue(oldRow.GameAccountValidationId.Value, out var prior) &&
                        prior.ProductId == variant.ProductId &&
                        prior.ExpiresAt > now && !prior.ConsumedAt.HasValue &&
                        !prior.OrderItemId.HasValue;
                    if (!priorUsable)
                    {
                        oldRow.GameAccountValidationId = entry.GameAccountValidationId;
                        oldRow.LineKey = BuildLineKey(entry.ProductVariantId, entry.GameAccountValidationId);
                    }
                }
                oldRow.UpdatedAt = now;
            }
            else
            {
                next[identity] = new CustomerCartItem
                {
                    CustomerUserId = customerId,
                    ProductVariantId = entry.ProductVariantId,
                    GameAccountValidationId = entry.GameAccountValidationId,
                    LineKey = BuildLineKey(entry.ProductVariantId, entry.GameAccountValidationId),
                    Quantity = entry.Quantity,
                    UpdatedAt = now
                };
            }
        }

        if (next.Count > 50)
            return Conflict(new { message = "Cart supports at most 50 distinct lines." });

        // Detect aliasing if an existing target has already received a newer
        // validation ID; only the canonical one remains.
        var kept = next.Values.ToHashSet();
        foreach (var old in existing.Where(x => !kept.Contains(x)))
            db.CustomerCartItems.Remove(old);
        foreach (var added in next.Values.Where(x => x.Id != Guid.Empty &&
            !existing.Any(old => old.Id == x.Id)))
            db.CustomerCartItems.Add(added);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        Response.Headers.CacheControl = "private, no-store";
        return Ok(new
        {
            count = next.Count,
            revision = ComputeCartRevision(next.Values.Select(x => (
                x.Id, x.ProductVariantId, x.GameAccountValidationId, x.Quantity, x.UpdatedAt)))
        });
    }

    private static string ComputeCartRevision(
        IEnumerable<(Guid Id, Guid VariantId, Guid? TargetId, int Quantity, DateTimeOffset UpdatedAt)> items)
    {
        var fingerprint = string.Join("|", items.OrderBy(x => x.Id)
            .Select(x => $"{x.Id:N}:{x.VariantId:N}:{x.TargetId?.ToString("N")}:{x.Quantity}:{x.UpdatedAt.UtcTicks}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)));
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
