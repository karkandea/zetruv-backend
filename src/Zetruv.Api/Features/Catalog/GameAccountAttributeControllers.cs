using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Catalog;

[ApiController]
[Authorize(Policy = AuthPolicies.CmsAdmin)]
[Route("api/v1/cms/catalog/games/{gameId:guid}/account-attributes")]
public sealed class CmsGameAccountAttributeController(ZetruvDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid gameId, CancellationToken ct)
    {
        if (!await db.Games.AnyAsync(x => x.Id == gameId, ct)) return NotFound();
        var schema = await db.GameAccountAttributeDefinitions.AsNoTracking()
            .Where(x => x.GameId == gameId)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Key).ToListAsync(ct);
        return Ok(schema.Select(GameAccountAttributeRules.ToResponse));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        Guid gameId, UpsertGameAccountAttributeRequest request, CancellationToken ct)
    {
        if (!await db.Games.AnyAsync(x => x.Id == gameId, ct)) return NotFound();
        var error = GameAccountAttributeRules.ValidateDefinition(request, out var options);
        if (error is not null) return BadRequest(new { message = error });
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({gameId.ToString()}))", ct);
        if (await db.GameAccountAttributeDefinitions.CountAsync(x => x.GameId == gameId, ct) >=
            GameAccountAttributeRules.MaxAttributes)
            return Conflict(new { message = "Maximum 30 attributes per game, including inactive attributes." });
        if (await db.GameAccountAttributeDefinitions.AnyAsync(
            x => x.GameId == gameId && x.Key == request.Key, ct))
            return Conflict(new { message = "Attribute key already exists in this game." });
        var definition = new GameAccountAttributeDefinition { GameId = gameId };
        Apply(definition, request, options);
        db.GameAccountAttributeDefinitions.Add(definition);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return StatusCode(201, GameAccountAttributeRules.ToResponse(definition));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid gameId, Guid id, UpsertGameAccountAttributeRequest request, CancellationToken ct)
    {
        var definition = await db.GameAccountAttributeDefinitions.SingleOrDefaultAsync(
            x => x.Id == id && x.GameId == gameId, ct);
        if (definition is null) return NotFound();
        var error = GameAccountAttributeRules.ValidateDefinition(request, out var options);
        if (error is not null) return BadRequest(new { message = error });
        if (definition.Key != request.Key)
            return Conflict(new { message = "Attribute keys are immutable. Create a new attribute instead." });

        var saved = await db.GameAccountDetails.AsNoTracking()
            .Where(x => x.Product.GameId == gameId)
            .Select(x => new { x.ProductId, x.AttributesJson })
            .ToListAsync(ct);
        // Do not invalidate existing listings when changing a schema.
        foreach (var row in saved)
        {
            var values = GameAccountAttributeRules.ParseValues(row.AttributesJson);
            if (values.TryGetValue(definition.Key, out var oldValue) &&
                definition.Type != request.Type)
                return Conflict(new { message = "Type cannot change once account values exist." });

            // Validate current value against edited option set and required flag.
            var candidate = new GameAccountAttributeDefinition
            {
                Id = id, GameId = gameId, Key = request.Key, Label = request.Label,
                Type = request.Type, OptionsJson = JsonSerializer.Serialize(options),
                IsRequired = request.IsRequired, IsActive = request.IsActive
            };
            var individual = values.Where(x => x.Key == definition.Key)
                .ToDictionary(x => x.Key, x => x.Value);
            var valueError = GameAccountAttributeRules.NormalizeValues(
                [candidate], individual, out _);
            if (request.IsActive && valueError is not null)
                return Conflict(new { message = $"Existing listing {row.ProductId} violates the updated attribute: {valueError}" });
        }
        Apply(definition, request, options);
        await db.SaveChangesAsync(ct);
        return Ok(GameAccountAttributeRules.ToResponse(definition));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Deactivate(Guid gameId, Guid id, CancellationToken ct)
    {
        var definition = await db.GameAccountAttributeDefinitions.SingleOrDefaultAsync(
            x => x.Id == id && x.GameId == gameId, ct);
        if (definition is null) return NotFound();
        // Soft deactivation keeps historical listing values and the key reserved.
        definition.IsActive = false;
        definition.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static void Apply(GameAccountAttributeDefinition definition,
        UpsertGameAccountAttributeRequest request, IReadOnlyList<string> options)
    {
        definition.Key = request.Key;
        definition.Label = request.Label.Trim();
        definition.Type = request.Type;
        definition.OptionsJson = JsonSerializer.Serialize(options);
        definition.IsRequired = request.IsRequired;
        definition.IsActive = request.IsActive;
        definition.ShowOnCard = request.ShowOnCard;
        definition.SortOrder = request.SortOrder;
        definition.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
