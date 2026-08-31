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

        var query = db.Products
            .AsNoTracking()
            .Where(x => x.IsActive && x.Category.IsActive);

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
                (x.Game != null && x.Game.Name.ToLower().Contains(term)));
        }

        var totalItems = await query.CountAsync(cancellationToken);
        var totalPages = totalItems == 0
            ? 0
            : (int)Math.Ceiling(totalItems / (double)pageSize);

        var items = await ProjectProductList(query)
            .OrderByDescending(x => x.IsFeatured)
            .ThenBy(x => x.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new ProductPageResponse(items, page, pageSize, totalItems, totalPages);
    }

    public async Task<IReadOnlyList<ProductListItemResponse>> GetProductsForHomepageAsync(
        ProductKind kind,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var query = db.Products
            .AsNoTracking()
            .Where(x => x.IsActive && x.Category.IsActive && x.Kind == kind);

        return await ProjectProductList(query)
            .OrderByDescending(x => x.IsFeatured)
            .ThenBy(x => x.Name)
            .Take(Math.Clamp(limit, 1, 50))
            .ToListAsync(cancellationToken);
    }

    public async Task<ProductDetailResponse?> GetProductBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        var normalized = CatalogText.NormalizeSlug(slug);

        var product = await db.Products
            .AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.Game)
            .Include(x => x.Variants)
            .Include(x => x.Images)
            .SingleOrDefaultAsync(
                x => x.Slug == normalized && x.IsActive && x.Category.IsActive,
                cancellationToken);

        if (product is null)
        {
            return null;
        }

        return new ProductDetailResponse(
            product.Id,
            product.Name,
            product.Slug,
            product.Kind,
            product.ShortDescription,
            product.Description,
            product.ThumbnailUrl,
            product.RequiresGameAccountValidation,
            product.IsFeatured,
            ToCategoryResponse(product.Category),
            product.Game is null ? null : ToGameResponse(product.Game),
            product.Variants
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .Select(x => new ProductVariantResponse(
                    x.Id,
                    x.Name,
                    x.Sku,
                    x.Price,
                    x.CompareAtPrice,
                    x.StockQuantity,
                    x.WeightGrams,
                    x.SortOrder))
                .ToList(),
            product.Images
                .OrderBy(x => x.SortOrder)
                .Select(x => new ProductImageResponse(
                    x.Id,
                    x.Url,
                    x.AltText,
                    x.SortOrder))
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

    private static IQueryable<ProductListItemResponse> ProjectProductList(
        IQueryable<Product> query) =>
        query.Select(x => new ProductListItemResponse(
            x.Id,
            x.Name,
            x.Slug,
            x.Kind,
            x.ThumbnailUrl,
            x.Category.Slug,
            x.Game == null ? null : x.Game.Name,
            x.Variants.Where(v => v.IsActive).Select(v => (decimal?)v.Price).Min(),
            x.Variants.Where(v => v.IsActive).Select(v => (decimal?)v.Price).Max(),
            x.IsFeatured));

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
