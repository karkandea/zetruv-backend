using System.ComponentModel.DataAnnotations;
using Zetruv.Api.Features.Articles;
using Zetruv.Api.Features.Catalog;
using Zetruv.Api.Features.Orders;

namespace Zetruv.Api.Features.Home;

public sealed class HomeHero
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string? PrimaryCtaLabel { get; set; }
    public string? PrimaryCtaUrl { get; set; }
    public string? SecondaryCtaLabel { get; set; }
    public string? SecondaryCtaUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class HomeSection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? CtaLabel { get; set; }
    public string? CtaUrl { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }
    public int ItemLimit { get; set; } = 10;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record HeroResponse(
    Guid Id,
    string Title,
    string Subtitle,
    string ImageUrl,
    string? PrimaryCtaLabel,
    string? PrimaryCtaUrl,
    string? SecondaryCtaLabel,
    string? SecondaryCtaUrl);

public sealed record SectionResponse(
    string Key,
    string Title,
    string? Subtitle,
    string? CtaLabel,
    string? CtaUrl,
    int ItemLimit);

public sealed record HomepageResponse(
    IReadOnlyList<HeroResponse> Heroes,
    IReadOnlyList<SectionResponse> Sections,
    IReadOnlyList<CategoryResponse> ServiceCategories,
    FlashSaleResponse? FlashSale,
    IReadOnlyList<GameResponse> PopularGames,
    IReadOnlyList<RecentPurchaseResponse> RecentlyPurchased,
    IReadOnlyList<ProductListItemResponse> Joki,
    IReadOnlyList<ProductListItemResponse> GameAccounts,
    IReadOnlyList<ProductListItemResponse> Merchandise,
    IReadOnlyList<ArticleListItemResponse> LatestArticles);

public sealed record UpsertHeroRequest(
    [Required, MaxLength(160)] string Title,
    [Required, MaxLength(500)] string Subtitle,
    [Required, MaxLength(1000)] string ImageUrl,
    [MaxLength(80)] string? PrimaryCtaLabel,
    [MaxLength(500)] string? PrimaryCtaUrl,
    [MaxLength(80)] string? SecondaryCtaLabel,
    [MaxLength(500)] string? SecondaryCtaUrl,
    bool IsActive,
    int SortOrder,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt);

public sealed record UpdateSectionRequest(
    [Required, MaxLength(160)] string Title,
    [MaxLength(500)] string? Subtitle,
    [MaxLength(80)] string? CtaLabel,
    [MaxLength(500)] string? CtaUrl,
    bool IsEnabled,
    int SortOrder,
    [Range(1, 50)] int ItemLimit);
