using System.ComponentModel.DataAnnotations;

namespace Zetruv.Api.Features.Site;

public enum FooterLinkGroup
{
    Page,
    Support,
    Legality
}

public sealed class SiteSetting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? LogoUrl { get; set; }
    public string BrandDescription { get; set; } = string.Empty;
    public string CopyrightText { get; set; } = string.Empty;
    public string ContactTeamLabel { get; set; } = "Contact our team";
    public string? ContactTeamUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SiteFooterLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public FooterLinkGroup Group { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SiteSocialLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Platform { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SitePaymentMethod
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record FooterLinkResponse(
    Guid Id,
    FooterLinkGroup Group,
    string Label,
    string Url);

public sealed record SocialLinkResponse(
    Guid Id,
    string Platform,
    string Url,
    string? IconUrl);

public sealed record PaymentMethodResponse(
    Guid Id,
    string Code,
    string Name,
    string? IconUrl);

public sealed record SiteFooterResponse(
    string? LogoUrl,
    string BrandDescription,
    string CopyrightText,
    string ContactTeamLabel,
    string? ContactTeamUrl,
    IReadOnlyList<FooterLinkResponse> Links,
    IReadOnlyList<SocialLinkResponse> Socials,
    IReadOnlyList<PaymentMethodResponse> PaymentMethods);

public sealed record UpdateSiteSettingsRequest(
    [MaxLength(1000)] string? LogoUrl,
    [Required, MaxLength(1000)] string BrandDescription,
    [Required, MaxLength(250)] string CopyrightText,
    [Required, MaxLength(80)] string ContactTeamLabel,
    [MaxLength(500)] string? ContactTeamUrl);

public sealed record UpsertFooterLinkRequest(
    FooterLinkGroup Group,
    [Required, MaxLength(100)] string Label,
    [Required, MaxLength(500)] string Url,
    bool IsActive,
    int SortOrder);

public sealed record UpsertSocialLinkRequest(
    [Required, MaxLength(80)] string Platform,
    [Required, MaxLength(500)] string Url,
    [MaxLength(1000)] string? IconUrl,
    bool IsActive,
    int SortOrder);

public sealed record UpsertPaymentMethodRequest(
    [Required, MaxLength(80)] string Code,
    [Required, MaxLength(120)] string Name,
    [MaxLength(1000)] string? IconUrl,
    bool IsActive,
    int SortOrder);
