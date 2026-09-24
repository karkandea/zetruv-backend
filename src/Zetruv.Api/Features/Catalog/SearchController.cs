using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Catalog;

public sealed record SearchSuggestion(Guid ProductId, string Name, string Slug,
    ProductKind Kind, string? ThumbnailUrl, string? GameName);
public sealed record SearchSuggestionResponse(
    string Query, int TotalMatches,
    IReadOnlyDictionary<string, IReadOnlyList<SearchSuggestion>> Groups,
    IReadOnlyList<string> Suggestions);

[ApiController]
[Route("api/v1/search")]
public sealed class SearchController(ZetruvDbContext db) : ControllerBase
{
    [HttpGet("suggestions")]
    public async Task<ActionResult<SearchSuggestionResponse>> Suggestions(
        [FromQuery(Name = "q")] string? query, CancellationToken ct)
    {
        var term = (query ?? "").Trim();
        if (term.Length < 3)
            return Ok(new SearchSuggestionResponse(term, 0,
                new Dictionary<string, IReadOnlyList<SearchSuggestion>>(), []));
        if (term.Length > 100)
            return BadRequest(new { message = "Search query must be 100 characters or fewer." });

        var products = db.Products.AsNoTracking()
            .Where(x => x.IsActive && x.Category.IsActive &&
                (x.Game == null || x.Game.IsActive) &&
                x.Variants.Any(v => v.IsActive) &&
                (EF.Functions.ILike(x.Name, "%" + term + "%") ||
                 (x.Game != null && EF.Functions.ILike(x.Game.Name, "%" + term + "%")) ||
                 (x.Game != null && x.Game.Publisher != null &&
                    EF.Functions.ILike(x.Game.Publisher, "%" + term + "%"))));
        var total = await products.CountAsync(ct);
        var results = await products.OrderByDescending(x => x.IsFeatured)
            .ThenBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Take(20)
            .Select(x => new SearchSuggestion(x.Id, x.Name, x.Slug,
                x.Kind, x.ThumbnailUrl, x.Game == null ? null : x.Game.Name))
            .ToListAsync(ct);
        var groups = results.GroupBy(x => x.Kind.ToString())
            .ToDictionary(x => x.Key,
                x => (IReadOnlyList<SearchSuggestion>)x.ToList());
        var suggestions = results
            .Select(x => x.GameName ?? x.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5).ToList();
        return Ok(new SearchSuggestionResponse(term, total, groups, suggestions));
    }
}
