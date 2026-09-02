using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Articles;
using Zetruv.Api.Features.Catalog;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Home;

public sealed class HomepageService(
    ZetruvDbContext db,
    CatalogService catalogService,
    ArticleService articleService)
{
    public async Task<HomepageResponse> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        var heroes = await db.HomeHeroes
            .AsNoTracking()
            .Where(x =>
                x.IsActive &&
                (x.StartsAt == null || x.StartsAt <= now) &&
                (x.EndsAt == null || x.EndsAt >= now))
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.CreatedAt)
            .Select(x => new HeroResponse(
                x.Id,
                x.Title,
                x.Subtitle,
                x.ImageUrl,
                x.PrimaryCtaLabel,
                x.PrimaryCtaUrl,
                x.SecondaryCtaLabel,
                x.SecondaryCtaUrl))
            .ToListAsync(cancellationToken);

        var sections = await db.HomeSections
            .AsNoTracking()
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.SortOrder)
            .Select(x => new SectionResponse(
                x.Key,
                x.Title,
                x.Subtitle,
                x.CtaLabel,
                x.CtaUrl,
                x.ItemLimit))
            .ToListAsync(cancellationToken);

        var limits = sections.ToDictionary(x => x.Key, x => x.ItemLimit);

        // These services share the same scoped DbContext, so EF operations stay sequential.
        var serviceCategories = await catalogService.GetActiveCategoriesAsync(
            Limit(limits, "service_categories", 10),
            cancellationToken);
        var flashSale = await catalogService.GetActiveFlashSaleAsync(
            Limit(limits, "flash_sale", 6),
            cancellationToken);
        var popularGames = await catalogService.GetGamesAsync(
            popularOnly: true,
            Limit(limits, "popular_games", 10),
            cancellationToken);
        var joki = await catalogService.GetProductsForHomepageAsync(
            ProductKind.Joki,
            Limit(limits, "joki", 10),
            cancellationToken);
        var gameAccounts = await catalogService.GetProductsForHomepageAsync(
            ProductKind.GameAccount,
            Limit(limits, "game_accounts", 3),
            cancellationToken);
        var merchandise = await catalogService.GetProductsForHomepageAsync(
            ProductKind.Merchandise,
            Limit(limits, "merchandise", 5),
            cancellationToken);
        var latestArticles = await articleService.GetLatestAsync(
            Limit(limits, "articles", 3),
            cancellationToken);

        return new HomepageResponse(
            heroes,
            sections,
            serviceCategories,
            flashSale,
            popularGames,
            joki,
            gameAccounts,
            merchandise,
            latestArticles);
    }

    private static int Limit(
        IReadOnlyDictionary<string, int> limits,
        string key,
        int fallback) =>
        limits.TryGetValue(key, out var value) ? value : fallback;
}

public sealed class HomepageSeeder(
    ZetruvDbContext db,
    ILogger<HomepageSeeder> logger)
{
    private static readonly HomeSection[] Defaults =
    [
        Section("service_categories", "Services", 10, 0),
        Section("flash_sale", "Flash Sale", 6, 10),
        Section("popular_games", "Game Populer", 10, 20),
        Section("recently_purchased", "Terakhir Dibeli", 5, 30),
        Section("joki", "Joki Game", 10, 40, ctaLabel: "Lihat Semua", ctaUrl: "/joki"),
        Section(
            "game_accounts",
            "Akun Game Pilihan",
            3,
            50,
            "Temukan akun sesuai rank, region, dan koleksi yang kamu cari.",
            "Lihat Semua Akun Game",
            "/game-account"),
        Section(
            "merchandise",
            "Merchandise",
            5,
            60,
            "Merchandise dan gaming gear untuk lengkapi setup kamu.",
            "Lihat Semua",
            "/merchandise"),
        Section(
            "articles",
            "Latest Articles",
            3,
            70,
            null,
            "View All Articles",
            "/articles")
    ];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await db.HomeSections.AnyAsync(cancellationToken))
        {
            return;
        }

        db.HomeSections.AddRange(Defaults);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded homepage section configuration.");
    }

    private static HomeSection Section(
        string key,
        string title,
        int itemLimit,
        int sortOrder,
        string? subtitle = null,
        string? ctaLabel = null,
        string? ctaUrl = null) =>
        new()
        {
            Key = key,
            Title = title,
            Subtitle = subtitle,
            CtaLabel = ctaLabel,
            CtaUrl = ctaUrl,
            ItemLimit = itemLimit,
            SortOrder = sortOrder,
            IsEnabled = true
        };
}
