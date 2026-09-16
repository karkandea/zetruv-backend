using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Catalog;

public sealed record ProviderSkuMappingResponse(
    Guid ProductId,
    string ProductName,
    Guid VariantId,
    string Nominal,
    string ZetruvSku,
    decimal SellingPrice,
    bool ProductActive,
    bool VariantActive,
    Guid? MappingId,
    string? ProviderSku,
    bool MappingActive,
    bool IsOperational);

public sealed record ProviderGameMappingResponse(
    Guid GameId,
    string GameName,
    FulfillmentMethod FulfillmentMethod,
    Guid? MappingId,
    string? ProviderCode,
    bool NicknameCheckEnabled,
    bool MappingActive,
    IReadOnlyList<ProviderSkuMappingResponse> Skus);

public sealed record UpsertProviderGameMappingRequest(
    [Required, MaxLength(80)] string ProviderCode,
    bool NicknameCheckEnabled,
    bool IsActive);

public sealed record UpsertProviderSkuMappingRequest(
    [Required, MaxLength(120)] string ProviderSku,
    bool IsActive);

[ApiController]
[Authorize(Policy = AuthPolicies.CmsAdmin)]
[Route("api/v1/cms/provider-mappings")]
public sealed class CmsProviderMappingsController(ZetruvDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProviderGameMappingResponse>>> GetMappings(
        CancellationToken cancellationToken)
    {
        var rows = await ReadRowsAsync(cancellationToken);
        var result = rows
            .GroupBy(x => new
            {
                x.GameId,
                x.GameName,
                x.GameMappingId,
                x.ProviderCode,
                x.NicknameCheckEnabled,
                x.GameMappingActive
            })
            .Select(group => new ProviderGameMappingResponse(
                group.Key.GameId,
                group.Key.GameName,
                FulfillmentMethod.AUTO_ID,
                group.Key.GameMappingId,
                group.Key.ProviderCode,
                group.Key.NicknameCheckEnabled,
                group.Key.GameMappingActive,
                group
                    .Where(x => x.VariantId.HasValue)
                    .Select(x => new ProviderSkuMappingResponse(
                        x.ProductId!.Value,
                        x.ProductName!,
                        x.VariantId!.Value,
                        x.Nominal!,
                        x.ZetruvSku!,
                        x.SellingPrice!.Value,
                        x.ProductActive,
                        x.VariantActive,
                        x.SkuMappingId,
                        x.ProviderSku,
                        x.SkuMappingActive,
                        group.Key.GameMappingActive &&
                            x.SkuMappingActive &&
                            x.ProductActive &&
                            x.VariantActive &&
                            !string.IsNullOrWhiteSpace(x.ProviderSku)))
                    .OrderBy(x => x.ProductName)
                    .ThenBy(x => x.Nominal)
                    .ToList()))
            .OrderBy(x => x.GameName)
            .ToList();

        return Ok(result);
    }

    [HttpPut("games/{gameId:guid}")]
    public async Task<IActionResult> UpsertGameMapping(
        Guid gameId,
        UpsertProviderGameMappingRequest request,
        CancellationToken cancellationToken)
    {
        var gameExists = await db.Games
            .AsNoTracking()
            .AnyAsync(x => x.Id == gameId, cancellationToken);
        if (!gameExists)
        {
            return NotFound();
        }

        var hasAutoIdProduct = await db.Products
            .AsNoTracking()
            .AnyAsync(
                x => x.GameId == gameId && x.FulfillmentMethod == FulfillmentMethod.AUTO_ID,
                cancellationToken);
        if (!hasAutoIdProduct)
        {
            return BadRequest(new
            {
                message = "Provider mapping is only available for games with AUTO_ID products."
            });
        }

        var providerCode = request.ProviderCode.Trim().ToLowerInvariant();
        if (providerCode.Length == 0)
        {
            return BadRequest(new { message = "Provider code is required." });
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO provider_game_mappings
                ("Id", "GameId", "ProviderCode", "NicknameCheckEnabled", "IsActive", "CreatedAt", "UpdatedAt")
            VALUES
                ({id}, {gameId}, {providerCode}, {request.NicknameCheckEnabled}, {request.IsActive}, {now}, {now})
            ON CONFLICT ("GameId") DO UPDATE SET
                "ProviderCode" = EXCLUDED."ProviderCode",
                "NicknameCheckEnabled" = EXCLUDED."NicknameCheckEnabled",
                "IsActive" = EXCLUDED."IsActive",
                "UpdatedAt" = EXCLUDED."UpdatedAt";
            """,
            cancellationToken);

        return NoContent();
    }

    [HttpPut("variants/{variantId:guid}")]
    public async Task<IActionResult> UpsertSkuMapping(
        Guid variantId,
        UpsertProviderSkuMappingRequest request,
        CancellationToken cancellationToken)
    {
        var variant = await db.ProductVariants
            .AsNoTracking()
            .Where(x => x.Id == variantId)
            .Select(x => new
            {
                x.Id,
                x.Product.GameId,
                x.Product.FulfillmentMethod
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (variant is null)
        {
            return NotFound();
        }

        if (variant.FulfillmentMethod != FulfillmentMethod.AUTO_ID || !variant.GameId.HasValue)
        {
            return BadRequest(new
            {
                message = "Provider SKU mapping is only available for AUTO_ID game variants."
            });
        }

        var providerSku = request.ProviderSku.Trim();
        if (providerSku.Length == 0)
        {
            return BadRequest(new { message = "Provider SKU is required." });
        }

        var gameMappingId = await GetGameMappingIdAsync(variant.GameId.Value, cancellationToken);
        if (!gameMappingId.HasValue)
        {
            return Conflict(new
            {
                message = "Configure the game provider mapping before mapping provider SKUs."
            });
        }

        if (await HasDuplicateProviderSkuAsync(
                gameMappingId.Value,
                variantId,
                providerSku,
                cancellationToken))
        {
            return Conflict(new
            {
                message = "Provider SKU is already mapped to another variant for this provider."
            });
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO provider_sku_mappings
                ("Id", "ProviderGameMappingId", "ProductVariantId", "ProviderSku", "IsActive", "CreatedAt", "UpdatedAt")
            VALUES
                ({id}, {gameMappingId.Value}, {variantId}, {providerSku}, {request.IsActive}, {now}, {now})
            ON CONFLICT ("ProductVariantId") DO UPDATE SET
                "ProviderGameMappingId" = EXCLUDED."ProviderGameMappingId",
                "ProviderSku" = EXCLUDED."ProviderSku",
                "IsActive" = EXCLUDED."IsActive",
                "UpdatedAt" = EXCLUDED."UpdatedAt";
            """,
            cancellationToken);

        return NoContent();
    }

    private async Task<Guid?> GetGameMappingIdAsync(
        Guid gameId,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var closeAfter = connection.State != ConnectionState.Open;
        if (closeAfter)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT \"Id\" FROM provider_game_mappings WHERE \"GameId\" = @gameId LIMIT 1";
            AddParameter(command, "@gameId", gameId);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value is Guid id ? id : null;
        }
        finally
        {
            if (closeAfter)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<bool> HasDuplicateProviderSkuAsync(
        Guid gameMappingId,
        Guid variantId,
        string providerSku,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var closeAfter = connection.State != ConnectionState.Open;
        if (closeAfter)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT EXISTS (
                    SELECT 1
                    FROM provider_sku_mappings
                    WHERE "ProviderGameMappingId" = @mappingId
                      AND "ProductVariantId" <> @variantId
                      AND LOWER("ProviderSku") = LOWER(@providerSku)
                )
                """;
            AddParameter(command, "@mappingId", gameMappingId);
            AddParameter(command, "@variantId", variantId);
            AddParameter(command, "@providerSku", providerSku);
            return await command.ExecuteScalarAsync(cancellationToken) is true;
        }
        finally
        {
            if (closeAfter)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<IReadOnlyList<ProviderMappingRow>> ReadRowsAsync(
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var closeAfter = connection.State != ConnectionState.Open;
        if (closeAfter)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    g."Id" AS "GameId",
                    g."Name" AS "GameName",
                    pgm."Id" AS "GameMappingId",
                    pgm."ProviderCode" AS "ProviderCode",
                    COALESCE(pgm."NicknameCheckEnabled", FALSE) AS "NicknameCheckEnabled",
                    COALESCE(pgm."IsActive", FALSE) AS "GameMappingActive",
                    p."Id" AS "ProductId",
                    p."Name" AS "ProductName",
                    p."IsActive" AS "ProductActive",
                    v."Id" AS "VariantId",
                    v."Name" AS "Nominal",
                    v."Sku" AS "ZetruvSku",
                    v."Price" AS "SellingPrice",
                    COALESCE(v."IsActive", FALSE) AS "VariantActive",
                    psm."Id" AS "SkuMappingId",
                    psm."ProviderSku" AS "ProviderSku",
                    COALESCE(psm."IsActive", FALSE) AS "SkuMappingActive"
                FROM games g
                INNER JOIN products p
                    ON p."GameId" = g."Id"
                   AND p."FulfillmentMethod" = 'AUTO_ID'
                LEFT JOIN product_variants v
                    ON v."ProductId" = p."Id"
                LEFT JOIN provider_game_mappings pgm
                    ON pgm."GameId" = g."Id"
                LEFT JOIN provider_sku_mappings psm
                    ON psm."ProductVariantId" = v."Id"
                ORDER BY g."SortOrder", g."Name", p."SortOrder", p."Name", v."SortOrder", v."Name";
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<ProviderMappingRow>();
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new ProviderMappingRow(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    ReadNullableGuid(reader, 2),
                    ReadNullableString(reader, 3),
                    reader.GetBoolean(4),
                    reader.GetBoolean(5),
                    reader.GetGuid(6),
                    reader.GetString(7),
                    reader.GetBoolean(8),
                    ReadNullableGuid(reader, 9),
                    ReadNullableString(reader, 10),
                    ReadNullableString(reader, 11),
                    ReadNullableDecimal(reader, 12),
                    reader.GetBoolean(13),
                    ReadNullableGuid(reader, 14),
                    ReadNullableString(reader, 15),
                    reader.GetBoolean(16)));
            }

            return rows;
        }
        finally
        {
            if (closeAfter)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static void AddParameter(
        System.Data.Common.DbCommand command,
        string name,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static Guid? ReadNullableGuid(System.Data.Common.DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);

    private static string? ReadNullableString(System.Data.Common.DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static decimal? ReadNullableDecimal(System.Data.Common.DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);

    private sealed record ProviderMappingRow(
        Guid GameId,
        string GameName,
        Guid? GameMappingId,
        string? ProviderCode,
        bool NicknameCheckEnabled,
        bool GameMappingActive,
        Guid? ProductId,
        string? ProductName,
        bool ProductActive,
        Guid? VariantId,
        string? Nominal,
        string? ZetruvSku,
        decimal? SellingPrice,
        bool VariantActive,
        Guid? SkuMappingId,
        string? ProviderSku,
        bool SkuMappingActive);
}
