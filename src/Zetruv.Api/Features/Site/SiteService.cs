using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Site;

public sealed class SiteService(ZetruvDbContext db)
{
    public async Task<SiteFooterResponse> GetFooterAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await db.SiteSettings
            .AsNoTracking()
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var links = await db.SiteFooterLinks
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Group)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Label)
            .Select(x => new FooterLinkResponse(x.Id, x.Group, x.Label, x.Url))
            .ToListAsync(cancellationToken);

        var socials = await db.SiteSocialLinks
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Platform)
            .Select(x => new SocialLinkResponse(x.Id, x.Platform, x.Url, x.IconUrl))
            .ToListAsync(cancellationToken);

        var payments = await db.SitePaymentMethods
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(x => new PaymentMethodResponse(x.Id, x.Code, x.Name, x.IconUrl))
            .ToListAsync(cancellationToken);

        return new SiteFooterResponse(
            settings?.LogoUrl,
            settings?.BrandDescription ?? string.Empty,
            settings?.CopyrightText ?? string.Empty,
            settings?.ContactTeamLabel ?? "Contact our team",
            settings?.ContactTeamUrl,
            links,
            socials,
            payments);
    }
}

public sealed class SiteSeeder(
    ZetruvDbContext db,
    ILogger<SiteSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await db.SiteSettings.AnyAsync(cancellationToken))
        {
            return;
        }

        db.SiteSettings.Add(new SiteSetting
        {
            BrandDescription = string.Empty,
            CopyrightText = "© CV Zetruv. All rights reserved.",
            ContactTeamLabel = "Contact our team"
        });

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded base site settings.");
    }
}
