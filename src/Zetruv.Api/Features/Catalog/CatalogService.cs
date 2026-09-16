using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Catalog;

public sealed class CatalogService(ZetruvDbContext db)
{
    public async Task<IReadOnlyList<CategoryResponse>> GetActiveCategoriesAsync(
        int limit = 50,
        CancellationToken cancellationToken = default) =>
        await db.CatalogCategories
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(x => new CategoryResponse(
                x.Id,
                x.Key,
                x.Name,
                x.Slug,
                x.Description,
                x.IconUrl,
                x.Kind,
                x.SortOrder))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<GameResponse>> GetGamesAsync(
        bool popularOnly = false,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var query = db.Games
            .AsNoTracking()
            .Where(x => x.IsActive);

        if (popularOnly)
        {
            query = query.Where(x => x.IsPopular);
        }

        return await query
            .OrderByDescending(x => x.IsPopular)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(x => new GameResponse(
                x.Id,
                x.Name,
                x.Slug,
                x.Publisher,
                x.ImageUrl,
                x.IsPopular,
                x.SortOrder))
            .ToListAsync(cancellationToken);
    }

    public async Task<ProductPageResponse> GetProductsAsync(
        string? categorySlug,
        string? gameSlug,
        ProductKind? kind,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = PublicProductsQuery();

        if (!string.IsNullOrWhiteSpace(categorySlug))
        {
            var normalized = CatalogText.NormalizeSlug(categorySlug);
            query = query.Where(x => x.Category.Slug == normalized);
        }

        if (!string.IsNullOrWhiteSpace(gameSlug))
        {
            var normalized = CatalogText.NormalizeSlug(gameSlug);
            query = query.Where(x => x.Game != null && x.Game.Slug == normalized);
        }

        if (kind.HasValue)
        {
            query = query.Where(x => x.Kind == kind.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x =>
                x.Name.ToLower().Contains(term) ||
                (x.Game != null && x.Game.Name.ToLower().Contains(term)) ||
                (x.Game != null && x.Game.Publisher != null && x.Game.Publisher.ToLower().Contains(term)));
        }

        var totalItems = await query.CountAsync(cancellationToken);
        var totalPages = totalItems == 0
            ? 0
            : (int)Math.Ceiling(totalItems / (double)pageSize);

        var orderedQuery = query
            .OrderByDescending(x => x.IsFeatured)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Name);

        var items = await GetProductListItemsAsync(
            orderedQuery,
            (page - 1) * pageSize,
            pageSize,
            DateTimeOffset.UtcNow,
            cancellationToken);

        return new ProductPageResponse(items, page, pageSize, totalItems, totalPages);
    }

    public async Task<IReadOnlyList<ProductListItemResponse>> GetProductsForHomepageAsync(
        ProductKind kind,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var query = PublicProductsQuery()
            .Where(x => x.Kind == kind)
            .OrderByDescending(x => x.IsFeatured)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Name);

        return await GetProductListItemsAsync(
            query,
            0,
            Math.Clamp(limit, 1, 50),
            DateTimeOffset.UtcNow,
            cancellationToken);
    }

    public async Task<ProductDetailResponse?> GetProductBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        var normalized = CatalogText.NormalizeSlug(slug);

        var product = await PublicProductsQuery()
            .Include(x => x.Category)
            .Include(x => x.Game)
            .Include(x => x.Variants)
            .Include(x => x.Images)
            .Include(x => x.InputFields)
            .SingleOrDefaultAsync(x => x.Slug == normalized, cancellationToken);

        if (product is null)
        {
            return null;
        }

        var variants = product.Variants
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToList();
        var offers = await LoadActiveSaleOffersAsync(
            variants.Select(x => x.Id),
            DateTimeOffset.UtcNow,
            cancellationToken);
        var variantResponses = variants
            .Select(x => ToProductVariantResponse(x, offers))
            .ToList();

        return new ProductDetailResponse(
            product.Id,
            product.Name,
            product.Slug,
            product.Kind,
            product.FulfillmentMethod,
            product.ShortDescription,
            product.Description,
            product.ThumbnailUrl,
            product.RequiresGameAccountValidation,
            product.IsFeatured,
            HasReadyInputSchema(product) && variantResponses.Any(x => x.IsAvailable),
            variantResponses.Any(x => x.IsOnSale),
            ToCategoryResponse(product.Category),
            product.Game is null ? null : ToGameResponse(product.Game),
            variantResponses,
            product.Images
                .OrderBy(x => x.SortOrder)
                .Select(x => new ProductImageResponse(
                    x.Id,
                    x.Url,
                    x.AltText,
                    x.SortOrder))
                .ToList(),
            product.InputFields
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Label)
                .Select(ProductInputFieldRules.ToResponse)
                .ToList());
    }

    public async Task<FlashSaleResponse?> GetActiveFlashSaleAsync(
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var promotion = await db.Promotions
            .AsNoTracking()
            .Include(x => x.Items)
                .ThenInclude(x => x.ProductVariant)
                    .ThenInclude(x => x.Product)
                        .ThenInclude(x => x.Game)
            .Where(x =>
                x.IsActive &&
                x.IsFlashSale &&
                x.StartsAt <= now &&
                x.EndsAt >= now)
            .OrderBy(x => x.EndsAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (promotion is null)
        {
            return null;
        }

        var items = promotion.Items
            .Where(x =>
                x.ProductVariant.IsActive &&
                x.ProductVariant.Product.IsActive &&
                (x.ProductVariant.Product.Game == null || x.ProductVariant.Product.Game.IsActive) &&
                x.SalePrice <= x.ProductVariant.Price)
            .OrderBy(x => x.SortOrder)
            .Take(Math.Clamp(limit, 1, 50))
            .Select(x => new FlashSaleItemResponse(
                x.Id,
                x.ProductVariant.ProductId,
                x.ProductVariantId,
                x.ProductVariant.Product.Name,
                x.ProductVariant.Product.Slug,
                x.ProductVariant.Name,
                x.ProductVariant.Product.ThumbnailUrl,
                x.ProductVariant.Product.Game?.Name,
                x.ProductVariant.Price,
                x.SalePrice,
                x.SortOrder))
            .ToList();

        return new FlashSaleResponse(
            promotion.Id,
            promotion.Name,
            promotion.StartsAt,
            promotion.EndsAt,
            items);
    }

    private IQueryable<Product> PublicProductsQuery() =>
        db.Products
            .AsNoTracking()
            .Where(x =>
                x.IsActive &&
                x.Category.IsActive &&
                (x.Game == null || x.Game.IsActive) &&
                x.Variants.Any(v => v.IsActive));

    private async Task<IReadOnlyList<ProductListItemResponse>> GetProductListItemsAsync(
        IQueryable<Product> query,
        int skip,
        int take,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var productIds = await query
            .Select(x => x.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        if (productIds.Count == 0)
        {
            return [];
        }

        var products = await db.Products
            .AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.Game)
            .Include(x => x.Variants)
            .Include(x => x.InputFields)
            .Where(x => productIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
        var productById = products.ToDictionary(x => x.Id);
        var offers = await LoadActiveSaleOffersAsync(
            products.SelectMany(x => x.Variants).Where(x => x.IsActive).Select(x => x.Id),
            now,
            cancellationToken);

        return productIds
            .Select(id => ToProductListItemResponse(productById[id], offers))
            .ToList();
    }

    private async Task<IReadOnlyDictionary<Guid, ActiveSaleOffer>> LoadActiveSaleOffersAsync(
        IEnumerable<Guid> variantIds,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var ids = variantIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, ActiveSaleOffer>();
        }

        var offers = await db.PromotionItems
            .AsNoTracking()
            .Where(x =>
                ids.Contains(x.ProductVariantId) &&
                x.Promotion.IsActive &&
                x.Promotion.IsFlashSale &&
                x.Promotion.StartsAt <= now &&
                x.Promotion.EndsAt >= now &&
                x.SalePrice <= x.ProductVariant.Price)
            .Select(x => new ActiveSaleOffer(
                x.ProductVariantId,
                x.SalePrice,
                x.Promotion.Name,
                x.Promotion.EndsAt))
            .ToListAsync(cancellationToken);

        return offers
            .GroupBy(x => x.VariantId)
            .ToDictionary(
                x => x.Key,
                x => x.OrderBy(o => o.SalePrice).ThenBy(o => o.EndsAt).First());
    }

    private static ProductListItemResponse ToProductListItemResponse(
        Product product,
        IReadOnlyDictionary<Guid, ActiveSaleOffer> offers)
    {
        var variants = product.Variants.Where(x => x.IsActive).ToList();
        var effectivePrices = variants
            .Select(x => offers.TryGetValue(x.Id, out var offer) ? offer.SalePrice : x.Price)
            .ToList();
        var regularPrices = variants.Select(x => x.Price).ToList();

        return new ProductListItemResponse(
            product.Id,
            product.Name,
            product.Slug,
            product.Kind,
            product.FulfillmentMethod,
            product.ThumbnailUrl,
            product.Category.Slug,
            product.Game?.Name,
            product.Game?.Slug,
            product.Game?.Publisher,
            effectivePrices.Count == 0 ? null : effectivePrices.Min(),
            effectivePrices.Count == 0 ? null : effectivePrices.Max(),
            regularPrices.Count == 0 ? null : regularPrices.Min(),
            regularPrices.Count == 0 ? null : regularPrices.Max(),
            variants.Count,
            HasReadyInputSchema(product) && variants.Any(IsVariantAvailable),
            variants.Any(x => offers.ContainsKey(x.Id)),
            product.IsFeatured);
    }

    private static ProductVariantResponse ToProductVariantResponse(
        ProductVariant variant,
        IReadOnlyDictionary<Guid, ActiveSaleOffer> offers)
    {
        var hasOffer = offers.TryGetValue(variant.Id, out var offer);
        return new ProductVariantResponse(
            variant.Id,
            variant.Name,
            variant.Sku,
            variant.Price,
            hasOffer ? offer!.SalePrice : variant.Price,
            variant.CompareAtPrice,
            variant.StockQuantity,
            variant.WeightGrams,
            IsVariantAvailable(variant),
            hasOffer,
            hasOffer ? offer!.PromotionName : null,
            hasOffer ? offer!.EndsAt : null,
            variant.SortOrder);
    }

    private static bool IsVariantAvailable(ProductVariant variant) =>
        !variant.StockQuantity.HasValue || variant.StockQuantity.Value > 0;

    private static bool HasReadyInputSchema(Product product) =>
        product.FulfillmentMethod switch
        {
            FulfillmentMethod.AUTO_ID => product.InputFields.Any(x =>
                x.Scope == ProductInputFieldScope.AccountValidation && x.IsRequired),
            FulfillmentMethod.MANUAL_LOGIN => product.InputFields.Any(x =>
                x.Scope == ProductInputFieldScope.LoginCredential && x.IsRequired),
            _ => true
        };

    internal static CategoryResponse ToCategoryResponse(CatalogCategory category) =>
        new(
            category.Id,
            category.Key,
            category.Name,
            category.Slug,
            category.Description,
            category.IconUrl,
            category.Kind,
            category.SortOrder);

    internal static GameResponse ToGameResponse(Game game) =>
        new(
            game.Id,
            game.Name,
            game.Slug,
            game.Publisher,
            game.ImageUrl,
            game.IsPopular,
            game.SortOrder);

    private sealed record ActiveSaleOffer(
        Guid VariantId,
        decimal SalePrice,
        string PromotionName,
        DateTimeOffset EndsAt);
}

public sealed class CatalogSeeder(
    ZetruvDbContext db,
    ILogger<CatalogSeeder> logger)
{
    private static readonly CatalogCategory[] Defaults =
    [
        Category("top_up_games", "Top Up Games", "top-up-games", ProductKind.TopUpGame, 0),
        Category("top_up_login", "Top Up Login", "top-up-login", ProductKind.TopUpLogin, 10),
        Category("voucher_game", "Voucher Game", "voucher-game", ProductKind.GameVoucher, 20),
        Category("joki_game", "Joki Game", "joki-game", ProductKind.Joki, 30),
        Category("merchandise", "Merchandise", "merchandise", ProductKind.Merchandise, 40),
        Category("game_account", "Game Account", "game-account", ProductKind.GameAccount, 50)
    ];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await db.CatalogCategories.AnyAsync(cancellationToken))
        {
            return;
        }

        db.CatalogCategories.AddRange(Defaults);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded default catalog categories.");
    }

    private static CatalogCategory Category(
        string key,
        string name,
        string slug,
        ProductKind kind,
        int sortOrder) =>
        new()
        {
            Key = key,
            Name = name,
            Slug = slug,
            Kind = kind,
            SortOrder = sortOrder,
            IsActive = true
        };
}

public static class CatalogText
{
    public static string NormalizeSlug(string value) =>
        string.Join(
            '-',
            value.Trim()
                .ToLowerInvariant()
                .Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries));

    public static string NormalizeKey(string value) =>
        string.Join(
            '_',
            value.Trim()
                .ToLowerInvariant()
                .Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries));

    public static string NormalizeSku(string value) =>
        value.Trim().ToUpperInvariant();
}
