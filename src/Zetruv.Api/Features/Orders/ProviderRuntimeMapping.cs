using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Orders;

public sealed record AutoIdRuntimeProviderMapping(
    string ProviderCode,
    string ProviderSku,
    bool NicknameCheckEnabled);

public sealed record AutoIdRuntimeProviderMappingResult(
    AutoIdRuntimeProviderMapping? Mapping,
    string? Error)
{
    public static AutoIdRuntimeProviderMappingResult Success(
        AutoIdRuntimeProviderMapping mapping) => new(mapping, null);

    public static AutoIdRuntimeProviderMappingResult Failure(string error) =>
        new(null, error);
}

public sealed class AutoIdRuntimeProviderMappingService(ZetruvDbContext db)
{
    public async Task<AutoIdRuntimeProviderMappingResult> ResolveAsync(
        Guid? productVariantId,
        CancellationToken cancellationToken = default)
    {
        if (!productVariantId.HasValue)
        {
            return AutoIdRuntimeProviderMappingResult.Failure(
                "AUTO_ID order item is missing its product variant mapping.");
        }

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    pgm."ProviderCode",
                    psm."ProviderSku",
                    pgm."NicknameCheckEnabled"
                FROM provider_sku_mappings psm
                JOIN provider_game_mappings pgm
                    ON pgm."Id" = psm."ProviderGameMappingId"
                JOIN product_variants pv
                    ON pv."Id" = psm."ProductVariantId"
                JOIN products p
                    ON p."Id" = pv."ProductId"
                   AND p."GameId" = pgm."GameId"
                WHERE psm."ProductVariantId" = @variantId
                  AND psm."IsActive" = TRUE
                  AND pgm."IsActive" = TRUE
                  AND p."FulfillmentMethod" = 'AUTO_ID'
                LIMIT 1;
                """;

            var parameter = command.CreateParameter();
            parameter.ParameterName = "variantId";
            parameter.Value = productVariantId.Value;
            command.Parameters.Add(parameter);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return AutoIdRuntimeProviderMappingResult.Failure(
                    "Active provider and provider SKU mappings are required for AUTO_ID fulfillment.");
            }

            var providerCode = reader.GetString(0).Trim().ToLowerInvariant();
            var providerSku = reader.GetString(1).Trim();
            if (providerCode.Length == 0 || providerSku.Length == 0)
            {
                return AutoIdRuntimeProviderMappingResult.Failure(
                    "Provider mapping is incomplete for AUTO_ID fulfillment.");
            }

            return AutoIdRuntimeProviderMappingResult.Success(
                new AutoIdRuntimeProviderMapping(
                    providerCode,
                    providerSku,
                    reader.GetBoolean(2)));
        }
        finally
        {
            if (openedHere)
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }
}
