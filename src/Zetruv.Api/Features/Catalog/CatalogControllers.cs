using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Catalog;

[ApiController]
[Route("api/v1/catalog")]
public sealed class CatalogController(CatalogService catalogService) : ControllerBase
{
    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<CategoryResponse>>> GetCategories(
        CancellationToken cancellationToken) =>
        Ok(await catalogService.GetActiveCategoriesAsync(cancellationToken: cancellationToken));

    [HttpGet("games")]
    public async Task<ActionResult<IReadOnlyList<GameResponse>>> GetGames(
        [FromQuery] bool popularOnly,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default) =>
        Ok(await catalogService.GetGamesAsync(popularOnly, limit, cancellationToken));

    [HttpGet("products")]
    public async Task<ActionResult<ProductPageResponse>> GetProducts(
        [FromQuery] string? category,
        [FromQuery] string? game,
        [FromQuery] ProductKind? kind,
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await catalogService.GetProductsAsync(
            category,
            game,
            kind,
            q,
            page,
            pageSize,
            cancellationToken));

    [HttpGet("products/{slug}")]
    public async Task<ActionResult<ProductDetailResponse>> GetProduct(
        string slug,
        CancellationToken cancellationToken)
    {
        var product = await catalogService.GetProductBySlugAsync(slug, cancellationToken);
        return product is null ? NotFound() : Ok(product);
    }

    [HttpGet("flash-sale")]
    public async Task<ActionResult<FlashSaleResponse>> GetFlashSale(
        [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var flashSale = await catalogService.GetActiveFlashSaleAsync(limit, cancellationToken);
        return flashSale is null ? NoContent() : Ok(flashSale);
    }
}

[ApiController]
[Authorize(Policy = AuthPolicies.CmsAdmin)]
[Route("api/v1/cms/catalog")]
public sealed class CmsCatalogController(ZetruvDbContext db) : ControllerBase
{
    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories(CancellationToken cancellationToken) =>
        Ok(await db.CatalogCategories
            .AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                x.Key,
                x.Name,
                x.Slug,
                x.Description,
                x.IconUrl,
                x.Kind,
                x.IsActive,
                x.SortOrder,
                x.CreatedAt,
                x.UpdatedAt
            })
            .ToListAsync(cancellationToken));

    [HttpPost("categories")]
    public async Task<IActionResult> CreateCategory(
        UpsertCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var key = CatalogText.NormalizeKey(request.Key);
        var slug = CatalogText.NormalizeSlug(request.Slug);

        if (await db.CatalogCategories.AnyAsync(
                x => x.Key == key || x.Slug == slug,
                cancellationToken))
        {
            return Conflict(new { message = "Category key or slug already exists." });
        }

        var category = new CatalogCategory();
        ApplyCategory(category, request, key, slug);
        db.CatalogCategories.Add(category);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetCategories), new { id = category.Id }, category.Id);
    }

    [HttpPut("categories/{id:guid}")]
    public async Task<IActionResult> UpdateCategory(
        Guid id,
        UpsertCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var category = await db.CatalogCategories.FindAsync([id], cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        var key = CatalogText.NormalizeKey(request.Key);
        var slug = CatalogText.NormalizeSlug(request.Slug);
        if (await db.CatalogCategories.AnyAsync(
                x => x.Id != id && (x.Key == key || x.Slug == slug),
                cancellationToken))
        {
            return Conflict(new { message = "Category key or slug already exists." });
        }

        var hasProductsWithDifferentKind = category.Kind != request.Kind &&
            await db.Products.AnyAsync(x => x.CategoryId == id, cancellationToken);
        if (hasProductsWithDifferentKind)
        {
            return Conflict(new { message = "Category kind cannot change after products exist." });
        }

        ApplyCategory(category, request, key, slug);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("categories/{id:guid}")]
    public async Task<IActionResult> DisableCategory(Guid id, CancellationToken cancellationToken)
    {
        var category = await db.CatalogCategories.FindAsync([id], cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        category.IsActive = false;
        category.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("games")]
    public async Task<IActionResult> GetGames(CancellationToken cancellationToken) =>
        Ok(await db.Games
            .AsNoTracking()
            .OrderByDescending(x => x.IsPopular)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken));

    [HttpPost("games")]
    public async Task<IActionResult> CreateGame(
        UpsertGameRequest request,
        CancellationToken cancellationToken)
    {
        var slug = CatalogText.NormalizeSlug(request.Slug);
        if (await db.Games.AnyAsync(x => x.Slug == slug, cancellationToken))
        {
            return Conflict(new { message = "Game slug already exists." });
        }

        var game = new Game();
        ApplyGame(game, request, slug);
        db.Games.Add(game);
        await db.SaveChangesAsync(cancellationToken);
        return Created($"/api/v1/cms/catalog/games/{game.Id}", game.Id);
    }

    [HttpPut("games/{id:guid}")]
    public async Task<IActionResult> UpdateGame(
        Guid id,
        UpsertGameRequest request,
        CancellationToken cancellationToken)
    {
        var game = await db.Games.FindAsync([id], cancellationToken);
        if (game is null)
        {
            return NotFound();
        }

        var slug = CatalogText.NormalizeSlug(request.Slug);
        if (await db.Games.AnyAsync(x => x.Id != id && x.Slug == slug, cancellationToken))
        {
            return Conflict(new { message = "Game slug already exists." });
        }

        ApplyGame(game, request, slug);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("games/{id:guid}")]
    public async Task<IActionResult> DisableGame(Guid id, CancellationToken cancellationToken)
    {
        var game = await db.Games.FindAsync([id], cancellationToken);
        if (game is null)
        {
            return NotFound();
        }

        game.IsActive = false;
        game.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("products")]
    public async Task<IActionResult> GetProducts(CancellationToken cancellationToken) =>
        Ok(await db.Products
            .AsNoTracking()
            .OrderByDescending(x => x.IsFeatured)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Slug,
                x.Kind,
                x.ThumbnailUrl,
                x.CategoryId,
                CategoryName = x.Category.Name,
                x.GameId,
                GameName = x.Game == null ? null : x.Game.Name,
                x.RequiresGameAccountValidation,
                x.IsActive,
                x.IsFeatured,
                x.SortOrder,
                ActiveVariantCount = x.Variants.Count(v => v.IsActive),
                MinPrice = x.Variants.Where(v => v.IsActive).Select(v => (decimal?)v.Price).Min(),
                MaxPrice = x.Variants.Where(v => v.IsActive).Select(v => (decimal?)v.Price).Max()
            })
            .ToListAsync(cancellationToken));

    [HttpGet("products/{id:guid}")]
    public async Task<IActionResult> GetProduct(Guid id, CancellationToken cancellationToken)
    {
        var product = await db.Products
            .AsNoTracking()
            .Include(x => x.Variants)
            .Include(x => x.Images)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return product is null ? NotFound() : Ok(product);
    }

    [HttpPost("products")]
    public async Task<IActionResult> CreateProduct(
        UpsertProductRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateProductRequest(request, null, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        var product = new Product();
        ApplyProduct(product, request, CatalogText.NormalizeSlug(request.Slug));
        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);
        return Created($"/api/v1/cms/catalog/products/{product.Id}", product.Id);
    }

    [HttpPut("products/{id:guid}")]
    public async Task<IActionResult> UpdateProduct(
        Guid id,
        UpsertProductRequest request,
        CancellationToken cancellationToken)
    {
        var product = await db.Products.FindAsync([id], cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        var validation = await ValidateProductRequest(request, id, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        ApplyProduct(product, request, CatalogText.NormalizeSlug(request.Slug));
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("products/{id:guid}")]
    public async Task<IActionResult> DisableProduct(Guid id, CancellationToken cancellationToken)
    {
        var product = await db.Products.FindAsync([id], cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        product.IsActive = false;
        product.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("products/{productId:guid}/variants")]
    public async Task<IActionResult> CreateVariant(
        Guid productId,
        UpsertVariantRequest request,
        CancellationToken cancellationToken)
    {
        if (!await db.Products.AnyAsync(x => x.Id == productId, cancellationToken))
        {
            return NotFound();
        }

        var sku = CatalogText.NormalizeSku(request.Sku);
        if (await db.ProductVariants.AnyAsync(x => x.Sku == sku, cancellationToken))
        {
            return Conflict(new { message = "SKU already exists." });
        }

        var validation = ValidateVariant(request);
        if (validation is not null)
        {
            return validation;
        }

        var variant = new ProductVariant { ProductId = productId };
        ApplyVariant(variant, request, sku);
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync(cancellationToken);
        return Created($"/api/v1/cms/catalog/products/{productId}/variants/{variant.Id}", variant.Id);
    }

    [HttpPut("products/{productId:guid}/variants/{variantId:guid}")]
    public async Task<IActionResult> UpdateVariant(
        Guid productId,
        Guid variantId,
        UpsertVariantRequest request,
        CancellationToken cancellationToken)
    {
        var variant = await db.ProductVariants.SingleOrDefaultAsync(
            x => x.Id == variantId && x.ProductId == productId,
            cancellationToken);
        if (variant is null)
        {
            return NotFound();
        }

        var sku = CatalogText.NormalizeSku(request.Sku);
        if (await db.ProductVariants.AnyAsync(
                x => x.Id != variantId && x.Sku == sku,
                cancellationToken))
        {
            return Conflict(new { message = "SKU already exists." });
        }

        var validation = ValidateVariant(request);
        if (validation is not null)
        {
            return validation;
        }

        ApplyVariant(variant, request, sku);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("products/{productId:guid}/variants/{variantId:guid}")]
    public async Task<IActionResult> DisableVariant(
        Guid productId,
        Guid variantId,
        CancellationToken cancellationToken)
    {
        var variant = await db.ProductVariants.SingleOrDefaultAsync(
            x => x.Id == variantId && x.ProductId == productId,
            cancellationToken);
        if (variant is null)
        {
            return NotFound();
        }

        variant.IsActive = false;
        variant.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("products/{productId:guid}/images")]
    public async Task<IActionResult> CreateImage(
        Guid productId,
        UpsertImageRequest request,
        CancellationToken cancellationToken)
    {
        if (!await db.Products.AnyAsync(x => x.Id == productId, cancellationToken))
        {
            return NotFound();
        }

        var image = new ProductImage
        {
            ProductId = productId,
            Url = request.Url.Trim(),
            AltText = request.AltText?.Trim(),
            SortOrder = request.SortOrder
        };
        db.ProductImages.Add(image);
        await db.SaveChangesAsync(cancellationToken);
        return Created($"/api/v1/cms/catalog/products/{productId}/images/{image.Id}", image.Id);
    }

    [HttpPut("products/{productId:guid}/images/{imageId:guid}")]
    public async Task<IActionResult> UpdateImage(
        Guid productId,
        Guid imageId,
        UpsertImageRequest request,
        CancellationToken cancellationToken)
    {
        var image = await db.ProductImages.SingleOrDefaultAsync(
            x => x.Id == imageId && x.ProductId == productId,
            cancellationToken);
        if (image is null)
        {
            return NotFound();
        }

        image.Url = request.Url.Trim();
        image.AltText = request.AltText?.Trim();
        image.SortOrder = request.SortOrder;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("products/{productId:guid}/images/{imageId:guid}")]
    public async Task<IActionResult> DeleteImage(
        Guid productId,
        Guid imageId,
        CancellationToken cancellationToken)
    {
        var image = await db.ProductImages.SingleOrDefaultAsync(
            x => x.Id == imageId && x.ProductId == productId,
            cancellationToken);
        if (image is null)
        {
            return NotFound();
        }

        db.ProductImages.Remove(image);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<IActionResult?> ValidateProductRequest(
        UpsertProductRequest request,
        Guid? currentProductId,
        CancellationToken cancellationToken)
    {
        var slug = CatalogText.NormalizeSlug(request.Slug);
        if (await db.Products.AnyAsync(
                x => x.Id != currentProductId && x.Slug == slug,
                cancellationToken))
        {
            return Conflict(new { message = "Product slug already exists." });
        }

        var category = await db.CatalogCategories
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.CategoryId, cancellationToken);
        if (category is null)
        {
            return BadRequest(new { message = "Category does not exist." });
        }

        if (category.Kind != request.Kind)
        {
            return BadRequest(new { message = "Product kind must match its category kind." });
        }

        if (request.GameId.HasValue &&
            !await db.Games.AnyAsync(x => x.Id == request.GameId.Value, cancellationToken))
        {
            return BadRequest(new { message = "Game does not exist." });
        }

        return null;
    }

    private IActionResult? ValidateVariant(UpsertVariantRequest request)
    {
        if (request.CompareAtPrice.HasValue && request.CompareAtPrice < request.Price)
        {
            return BadRequest(new { message = "Compare-at price cannot be lower than price." });
        }

        if (request.StockQuantity < 0 || request.WeightGrams < 0)
        {
            return BadRequest(new { message = "Stock quantity and weight cannot be negative." });
        }

        return null;
    }

    private static void ApplyCategory(
        CatalogCategory category,
        UpsertCategoryRequest request,
        string key,
        string slug)
    {
        category.Key = key;
        category.Name = request.Name.Trim();
        category.Slug = slug;
        category.Description = request.Description?.Trim();
        category.IconUrl = request.IconUrl?.Trim();
        category.Kind = request.Kind;
        category.IsActive = request.IsActive;
        category.SortOrder = request.SortOrder;
        category.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void ApplyGame(Game game, UpsertGameRequest request, string slug)
    {
        game.Name = request.Name.Trim();
        game.Slug = slug;
        game.Publisher = request.Publisher?.Trim();
        game.ImageUrl = request.ImageUrl?.Trim();
        game.IsActive = request.IsActive;
        game.IsPopular = request.IsPopular;
        game.SortOrder = request.SortOrder;
        game.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void ApplyProduct(Product product, UpsertProductRequest request, string slug)
    {
        product.CategoryId = request.CategoryId;
        product.GameId = request.GameId;
        product.Name = request.Name.Trim();
        product.Slug = slug;
        product.ShortDescription = request.ShortDescription?.Trim();
        product.Description = request.Description?.Trim();
        product.ThumbnailUrl = request.ThumbnailUrl?.Trim();
        product.Kind = request.Kind;
        product.RequiresGameAccountValidation = request.RequiresGameAccountValidation;
        product.IsActive = request.IsActive;
        product.IsFeatured = request.IsFeatured;
        product.SortOrder = request.SortOrder;
        product.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void ApplyVariant(ProductVariant variant, UpsertVariantRequest request, string sku)
    {
        variant.Name = request.Name.Trim();
        variant.Sku = sku;
        variant.Price = request.Price;
        variant.CompareAtPrice = request.CompareAtPrice;
        variant.StockQuantity = request.StockQuantity;
        variant.WeightGrams = request.WeightGrams;
        variant.IsActive = request.IsActive;
        variant.SortOrder = request.SortOrder;
        variant.UpdatedAt = DateTimeOffset.UtcNow;
    }
}

[ApiController]
[Authorize(Policy = AuthPolicies.CmsAdmin)]
[Route("api/v1/cms/promotions")]
public sealed class CmsPromotionsController(ZetruvDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPromotions(CancellationToken cancellationToken) =>
        Ok(await db.Promotions
            .AsNoTracking()
            .Include(x => x.Items)
            .OrderByDescending(x => x.StartsAt)
            .ToListAsync(cancellationToken));

    [HttpPost]
    public async Task<IActionResult> CreatePromotion(
        UpsertPromotionRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidatePromotion(request, null, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        var promotion = new Promotion();
        ApplyPromotion(promotion, request);
        db.Promotions.Add(promotion);
        await db.SaveChangesAsync(cancellationToken);
        return Created($"/api/v1/cms/promotions/{promotion.Id}", promotion.Id);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdatePromotion(
        Guid id,
        UpsertPromotionRequest request,
        CancellationToken cancellationToken)
    {
        var promotion = await db.Promotions
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (promotion is null)
        {
            return NotFound();
        }

        var validation = await ValidatePromotion(request, id, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        db.PromotionItems.RemoveRange(promotion.Items);
        promotion.Items.Clear();
        ApplyPromotion(promotion, request);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DisablePromotion(Guid id, CancellationToken cancellationToken)
    {
        var promotion = await db.Promotions.FindAsync([id], cancellationToken);
        if (promotion is null)
        {
            return NotFound();
        }

        promotion.IsActive = false;
        promotion.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<IActionResult?> ValidatePromotion(
        UpsertPromotionRequest request,
        Guid? currentPromotionId,
        CancellationToken cancellationToken)
    {
        if (request.EndsAt <= request.StartsAt)
        {
            return BadRequest(new { message = "Promotion end time must be after start time." });
        }

        var slug = CatalogText.NormalizeSlug(request.Slug);
        if (await db.Promotions.AnyAsync(
                x => x.Id != currentPromotionId && x.Slug == slug,
                cancellationToken))
        {
            return Conflict(new { message = "Promotion slug already exists." });
        }

        if (request.Items.Select(x => x.ProductVariantId).Distinct().Count() != request.Items.Count)
        {
            return BadRequest(new { message = "A variant can only appear once in a promotion." });
        }

        var variantIds = request.Items.Select(x => x.ProductVariantId).ToArray();
        var variants = await db.ProductVariants
            .AsNoTracking()
            .Where(x => variantIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        if (variants.Count != variantIds.Length)
        {
            return BadRequest(new { message = "One or more promotion variants do not exist." });
        }

        foreach (var item in request.Items)
        {
            if (item.SalePrice > variants[item.ProductVariantId].Price)
            {
                return BadRequest(new { message = "Flash-sale price cannot exceed the normal price." });
            }
        }

        return null;
    }

    private static void ApplyPromotion(Promotion promotion, UpsertPromotionRequest request)
    {
        promotion.Name = request.Name.Trim();
        promotion.Slug = CatalogText.NormalizeSlug(request.Slug);
        promotion.IsFlashSale = request.IsFlashSale;
        promotion.IsActive = request.IsActive;
        promotion.StartsAt = request.StartsAt;
        promotion.EndsAt = request.EndsAt;
        promotion.UpdatedAt = DateTimeOffset.UtcNow;

        foreach (var item in request.Items.OrderBy(x => x.SortOrder))
        {
            promotion.Items.Add(new PromotionItem
            {
                ProductVariantId = item.ProductVariantId,
                SalePrice = item.SalePrice,
                SortOrder = item.SortOrder
            });
        }
    }
}
