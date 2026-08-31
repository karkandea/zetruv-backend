using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Home
{
    public sealed class HomepageService(ZetruvDbContext db)
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

            return new HomepageResponse(heroes, sections);
        }
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
}
