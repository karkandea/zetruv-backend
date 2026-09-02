using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Site;

[ApiController]
[Route("api/v1/site")]
public sealed class SiteController(SiteService siteService) : ControllerBase
{
    [HttpGet("footer")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<SiteFooterResponse>> GetFooter(
        CancellationToken cancellationToken) =>
        Ok(await siteService.GetFooterAsync(cancellationToken));
}

[ApiController]
[Authorize(Policy = AuthPolicies.CmsAdmin)]
[Route("api/v1/cms/site")]
public sealed class CmsSiteController(ZetruvDbContext db) : ControllerBase
{
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken)
    {
        var settings = await GetOrCreateSettingsAsync(cancellationToken);
        return Ok(settings);
    }

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings(
        UpdateSiteSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await GetOrCreateSettingsAsync(cancellationToken);
        settings.LogoUrl = request.LogoUrl?.Trim();
        settings.BrandDescription = request.BrandDescription.Trim();
        settings.CopyrightText = request.CopyrightText.Trim();
        settings.ContactTeamLabel = request.ContactTeamLabel.Trim();
        settings.ContactTeamUrl = request.ContactTeamUrl?.Trim();
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("footer-links")]
    public async Task<IActionResult> GetFooterLinks(CancellationToken cancellationToken) =>
        Ok(await db.SiteFooterLinks
            .AsNoTracking()
            .OrderBy(x => x.Group)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Label)
            .ToListAsync(cancellationToken));

    [HttpPost("footer-links")]
    public async Task<IActionResult> CreateFooterLink(
        UpsertFooterLinkRequest request,
        CancellationToken cancellationToken)
    {
        var link = new SiteFooterLink();
        ApplyFooterLink(link, request);
        db.SiteFooterLinks.Add(link);
        await db.SaveChangesAsync(cancellationToken);
        return Created($"/api/v1/cms/site/footer-links/{link.Id}", link.Id);
    }

    [HttpPut("footer-links/{id:guid}")]
    public async Task<IActionResult> UpdateFooterLink(
        Guid id,
        UpsertFooterLinkRequest request,
        CancellationToken cancellationToken)
    {
        var link = await db.SiteFooterLinks.FindAsync([id], cancellationToken);
        if (link is null)
        {
            return NotFound();
        }

        ApplyFooterLink(link, request);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("footer-links/{id:guid}")]
    public async Task<IActionResult> DeleteFooterLink(Guid id, CancellationToken cancellationToken)
    {
        var link = await db.SiteFooterLinks.FindAsync([id], cancellationToken);
        if (link is null)
        {
            return NotFound();
        }

        db.SiteFooterLinks.Remove(link);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("social-links")]
    public async Task<IActionResult> GetSocialLinks(CancellationToken cancellationToken) =>
        Ok(await db.SiteSocialLinks
            .AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Platform)
            .ToListAsync(cancellationToken));

    [HttpPost("social-links")]
    public async Task<IActionResult> CreateSocialLink(
        UpsertSocialLinkRequest request,
        CancellationToken cancellationToken)
    {
        var social = new SiteSocialLink();
        ApplySocialLink(social, request);
        db.SiteSocialLinks.Add(social);
        await db.SaveChangesAsync(cancellationToken);
        return Created($"/api/v1/cms/site/social-links/{social.Id}", social.Id);
    }

    [HttpPut("social-links/{id:guid}")]
    public async Task<IActionResult> UpdateSocialLink(
        Guid id,
        UpsertSocialLinkRequest request,
        CancellationToken cancellationToken)
    {
        var social = await db.SiteSocialLinks.FindAsync([id], cancellationToken);
        if (social is null)
        {
            return NotFound();
        }

        ApplySocialLink(social, request);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("social-links/{id:guid}")]
    public async Task<IActionResult> DeleteSocialLink(Guid id, CancellationToken cancellationToken)
    {
        var social = await db.SiteSocialLinks.FindAsync([id], cancellationToken);
        if (social is null)
        {
            return NotFound();
        }

        db.SiteSocialLinks.Remove(social);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("payment-methods")]
    public async Task<IActionResult> GetPaymentMethods(CancellationToken cancellationToken) =>
        Ok(await db.SitePaymentMethods
            .AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken));

    [HttpPost("payment-methods")]
    public async Task<IActionResult> CreatePaymentMethod(
        UpsertPaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToLowerInvariant();
        if (await db.SitePaymentMethods.AnyAsync(x => x.Code == code, cancellationToken))
        {
            return Conflict(new { message = "Payment method code already exists." });
        }

        var payment = new SitePaymentMethod();
        ApplyPaymentMethod(payment, request, code);
        db.SitePaymentMethods.Add(payment);
        await db.SaveChangesAsync(cancellationToken);
        return Created($"/api/v1/cms/site/payment-methods/{payment.Id}", payment.Id);
    }

    [HttpPut("payment-methods/{id:guid}")]
    public async Task<IActionResult> UpdatePaymentMethod(
        Guid id,
        UpsertPaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        var payment = await db.SitePaymentMethods.FindAsync([id], cancellationToken);
        if (payment is null)
        {
            return NotFound();
        }

        var code = request.Code.Trim().ToLowerInvariant();
        if (await db.SitePaymentMethods.AnyAsync(
                x => x.Id != id && x.Code == code,
                cancellationToken))
        {
            return Conflict(new { message = "Payment method code already exists." });
        }

        ApplyPaymentMethod(payment, request, code);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("payment-methods/{id:guid}")]
    public async Task<IActionResult> DeletePaymentMethod(Guid id, CancellationToken cancellationToken)
    {
        var payment = await db.SitePaymentMethods.FindAsync([id], cancellationToken);
        if (payment is null)
        {
            return NotFound();
        }

        db.SitePaymentMethods.Remove(payment);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<SiteSetting> GetOrCreateSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await db.SiteSettings
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (settings is not null)
        {
            return settings;
        }

        settings = new SiteSetting
        {
            CopyrightText = "© CV Zetruv. All rights reserved.",
            ContactTeamLabel = "Contact our team"
        };
        db.SiteSettings.Add(settings);
        await db.SaveChangesAsync(cancellationToken);
        return settings;
    }

    private static void ApplyFooterLink(SiteFooterLink link, UpsertFooterLinkRequest request)
    {
        link.Group = request.Group;
        link.Label = request.Label.Trim();
        link.Url = request.Url.Trim();
        link.IsActive = request.IsActive;
        link.SortOrder = request.SortOrder;
        link.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void ApplySocialLink(SiteSocialLink social, UpsertSocialLinkRequest request)
    {
        social.Platform = request.Platform.Trim();
        social.Url = request.Url.Trim();
        social.IconUrl = request.IconUrl?.Trim();
        social.IsActive = request.IsActive;
        social.SortOrder = request.SortOrder;
        social.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void ApplyPaymentMethod(
        SitePaymentMethod payment,
        UpsertPaymentMethodRequest request,
        string code)
    {
        payment.Code = code;
        payment.Name = request.Name.Trim();
        payment.IconUrl = request.IconUrl?.Trim();
        payment.IsActive = request.IsActive;
        payment.SortOrder = request.SortOrder;
        payment.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
