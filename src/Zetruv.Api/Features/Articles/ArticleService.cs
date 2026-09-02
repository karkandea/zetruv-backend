using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Articles;

public sealed class ArticleService(ZetruvDbContext db)
{
    public async Task<IReadOnlyList<ArticleCategoryResponse>> GetCategoriesAsync(
        CancellationToken cancellationToken = default) =>
        await db.ArticleCategories
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(x => new ArticleCategoryResponse(x.Id, x.Name, x.Slug))
            .ToListAsync(cancellationToken);

    public async Task<ArticlePageResponse> GetPublishedAsync(
        string? category,
        string? query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var now = DateTimeOffset.UtcNow;

        var articles = db.Articles
            .AsNoTracking()
            .Where(x => x.IsPublished && x.PublishedAt != null && x.PublishedAt <= now);

        if (!string.IsNullOrWhiteSpace(category))
        {
            var normalizedCategory = ArticleText.NormalizeSlug(category);
            articles = articles.Where(x => x.Category.Slug == normalizedCategory);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            articles = articles.Where(x =>
                EF.Functions.ILike(x.Title, $"%{term}%") ||
                EF.Functions.ILike(x.Excerpt, $"%{term}%"));
        }

        var totalItems = await articles.CountAsync(cancellationToken);
        var pageQuery = articles
            .OrderByDescending(x => x.PublishedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
        var items = await ProjectList(pageQuery)
            .ToListAsync(cancellationToken);

        var totalPages = totalItems == 0
            ? 0
            : (int)Math.Ceiling(totalItems / (double)pageSize);

        return new ArticlePageResponse(items, page, pageSize, totalItems, totalPages);
    }

    public async Task<ArticleDetailResponse?> GetBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        var normalizedSlug = ArticleText.NormalizeSlug(slug);
        var now = DateTimeOffset.UtcNow;

        return await db.Articles
            .AsNoTracking()
            .Where(x =>
                x.Slug == normalizedSlug &&
                x.IsPublished &&
                x.PublishedAt != null &&
                x.PublishedAt <= now)
            .Select(x => new ArticleDetailResponse(
                x.Id,
                x.Title,
                x.Slug,
                x.Excerpt,
                x.Content,
                x.ThumbnailUrl,
                x.AuthorName,
                new ArticleCategoryResponse(x.Category.Id, x.Category.Name, x.Category.Slug),
                x.PublishedAt!.Value))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ArticleListItemResponse>> GetLatestAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        limit = Math.Clamp(limit, 1, 20);

        var latestArticles = db.Articles
            .AsNoTracking()
            .Where(x => x.IsPublished && x.PublishedAt != null && x.PublishedAt <= now)
            .OrderByDescending(x => x.PublishedAt)
            .Take(limit);

        return await ProjectList(latestArticles)
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<ArticleListItemResponse> ProjectList(IQueryable<Article> query) =>
        query.Select(x => new ArticleListItemResponse(
            x.Id,
            x.Title,
            x.Slug,
            x.Excerpt,
            x.ThumbnailUrl,
            x.AuthorName,
            new ArticleCategoryResponse(x.Category.Id, x.Category.Name, x.Category.Slug),
            x.PublishedAt!.Value));
}
