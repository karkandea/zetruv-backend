using System.ComponentModel.DataAnnotations;

namespace Zetruv.Api.Features.Articles;

public sealed class ArticleCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<Article> Articles { get; set; } = new List<Article>();
}

public sealed class Article
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CategoryId { get; set; }
    public ArticleCategory Category { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Excerpt { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string? AuthorName { get; set; }
    public bool IsPublished { get; set; }
    public bool IsFeatured { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record ArticleCategoryResponse(Guid Id, string Name, string Slug);

public sealed record ArticleListItemResponse(
    Guid Id,
    string Title,
    string Slug,
    string Excerpt,
    string ThumbnailUrl,
    string? AuthorName,
    ArticleCategoryResponse Category,
    DateTimeOffset PublishedAt);

public sealed record ArticleDetailResponse(
    Guid Id,
    string Title,
    string Slug,
    string Excerpt,
    string Content,
    string ThumbnailUrl,
    string? AuthorName,
    ArticleCategoryResponse Category,
    DateTimeOffset PublishedAt);

public sealed record ArticlePageResponse(
    IReadOnlyList<ArticleListItemResponse> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

public sealed record UpsertArticleCategoryRequest(
    [property: Required, MaxLength(120)] string Name,
    [property: Required, MaxLength(160)] string Slug,
    bool IsActive,
    int SortOrder);

public sealed record UpsertArticleRequest(
    [property: Required] Guid CategoryId,
    [property: Required, MaxLength(220)] string Title,
    [property: Required, MaxLength(240)] string Slug,
    [property: Required, MaxLength(600)] string Excerpt,
    [property: Required] string Content,
    [property: Required, MaxLength(1000)] string ThumbnailUrl,
    [property: MaxLength(120)] string? AuthorName,
    bool IsPublished,
    bool IsFeatured,
    DateTimeOffset? PublishedAt);

public static class ArticleText
{
    public static string NormalizeSlug(string value) =>
        value.Trim().ToLowerInvariant()
            .Replace("_", "-")
            .Replace(" ", "-");
}
