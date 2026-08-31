using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Articles;

[ApiController]
[Route("api/v1/articles")]
public sealed class ArticlesController(ArticleService articleService) : ControllerBase
{
    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<ArticleCategoryResponse>>> GetCategories(
        CancellationToken cancellationToken) =>
        Ok(await articleService.GetCategoriesAsync(cancellationToken));

    [HttpGet]
    public async Task<ActionResult<ArticlePageResponse>> GetArticles(
        [FromQuery] string? category,
        [FromQuery(Name = "q")] string? query,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12,
        CancellationToken cancellationToken = default) =>
        Ok(await articleService.GetPublishedAsync(
            category,
            query,
            page,
            pageSize,
            cancellationToken));

    [HttpGet("{slug}")]
    public async Task<ActionResult<ArticleDetailResponse>> GetArticle(
        string slug,
        CancellationToken cancellationToken)
    {
        var article = await articleService.GetBySlugAsync(slug, cancellationToken);
        return article is null ? NotFound() : Ok(article);
    }
}

[ApiController]
[Authorize(Policy = AuthPolicies.CmsAdmin)]
[Route("api/v1/cms/articles")]
public sealed class CmsArticlesController(ZetruvDbContext db) : ControllerBase
{
    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories(CancellationToken cancellationToken) =>
        Ok(await db.ArticleCategories
            .AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Slug,
                x.IsActive,
                x.SortOrder,
                ArticleCount = x.Articles.Count,
                x.CreatedAt,
                x.UpdatedAt
            })
            .ToListAsync(cancellationToken));

    [HttpPost("categories")]
    public async Task<IActionResult> CreateCategory(
        UpsertArticleCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var slug = ArticleText.NormalizeSlug(request.Slug);
        if (await db.ArticleCategories.AnyAsync(x => x.Slug == slug, cancellationToken))
        {
            return Conflict(new { message = "Article category slug already exists." });
        }

        var category = new ArticleCategory();
        ApplyCategory(category, request, slug);
        db.ArticleCategories.Add(category);
        await db.SaveChangesAsync(cancellationToken);
        return Created($"/api/v1/cms/articles/categories/{category.Id}", category.Id);
    }

    [HttpPut("categories/{id:guid}")]
    public async Task<IActionResult> UpdateCategory(
        Guid id,
        UpsertArticleCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var category = await db.ArticleCategories.FindAsync([id], cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        var slug = ArticleText.NormalizeSlug(request.Slug);
        if (await db.ArticleCategories.AnyAsync(
                x => x.Id != id && x.Slug == slug,
                cancellationToken))
        {
            return Conflict(new { message = "Article category slug already exists." });
        }

        ApplyCategory(category, request, slug);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("categories/{id:guid}")]
    public async Task<IActionResult> DisableCategory(Guid id, CancellationToken cancellationToken)
    {
        var category = await db.ArticleCategories.FindAsync([id], cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        category.IsActive = false;
        category.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet]
    public async Task<IActionResult> GetArticles(CancellationToken cancellationToken) =>
        Ok(await db.Articles
            .AsNoTracking()
            .OrderByDescending(x => x.PublishedAt ?? x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.Slug,
                x.Excerpt,
                x.ThumbnailUrl,
                x.AuthorName,
                x.CategoryId,
                CategoryName = x.Category.Name,
                x.IsPublished,
                x.IsFeatured,
                x.PublishedAt,
                x.CreatedAt,
                x.UpdatedAt
            })
            .ToListAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetArticle(Guid id, CancellationToken cancellationToken)
    {
        var article = await db.Articles
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return article is null ? NotFound() : Ok(article);
    }

    [HttpPost]
    public async Task<IActionResult> CreateArticle(
        UpsertArticleRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateArticleRequest(request, null, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        var article = new Article();
        ApplyArticle(article, request, ArticleText.NormalizeSlug(request.Slug));
        db.Articles.Add(article);
        await db.SaveChangesAsync(cancellationToken);
        return Created($"/api/v1/cms/articles/{article.Id}", article.Id);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateArticle(
        Guid id,
        UpsertArticleRequest request,
        CancellationToken cancellationToken)
    {
        var article = await db.Articles.FindAsync([id], cancellationToken);
        if (article is null)
        {
            return NotFound();
        }

        var validation = await ValidateArticleRequest(request, id, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        ApplyArticle(article, request, ArticleText.NormalizeSlug(request.Slug));
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteArticle(Guid id, CancellationToken cancellationToken)
    {
        var article = await db.Articles.FindAsync([id], cancellationToken);
        if (article is null)
        {
            return NotFound();
        }

        db.Articles.Remove(article);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<IActionResult?> ValidateArticleRequest(
        UpsertArticleRequest request,
        Guid? currentId,
        CancellationToken cancellationToken)
    {
        if (!await db.ArticleCategories.AnyAsync(
                x => x.Id == request.CategoryId && x.IsActive,
                cancellationToken))
        {
            return BadRequest(new { message = "Article category does not exist or is inactive." });
        }

        var slug = ArticleText.NormalizeSlug(request.Slug);
        if (await db.Articles.AnyAsync(
                x => x.Id != currentId && x.Slug == slug,
                cancellationToken))
        {
            return Conflict(new { message = "Article slug already exists." });
        }

        if (request.IsPublished && request.PublishedAt is null)
        {
            return BadRequest(new { message = "PublishedAt is required when publishing an article." });
        }

        return null;
    }

    private static void ApplyCategory(
        ArticleCategory category,
        UpsertArticleCategoryRequest request,
        string slug)
    {
        category.Name = request.Name.Trim();
        category.Slug = slug;
        category.IsActive = request.IsActive;
        category.SortOrder = request.SortOrder;
        category.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void ApplyArticle(
        Article article,
        UpsertArticleRequest request,
        string slug)
    {
        article.CategoryId = request.CategoryId;
        article.Title = request.Title.Trim();
        article.Slug = slug;
        article.Excerpt = request.Excerpt.Trim();
        article.Content = request.Content.Trim();
        article.ThumbnailUrl = request.ThumbnailUrl.Trim();
        article.AuthorName = request.AuthorName?.Trim();
        article.IsPublished = request.IsPublished;
        article.IsFeatured = request.IsFeatured;
        article.PublishedAt = request.PublishedAt;
        article.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
