using System.Data;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Orders;

public static class ProductionProviderSafety
{
    public static async Task EnsureAutoIdMappingsAreSafeAsync(
        ZetruvDbContext db,
        CancellationToken cancellationToken = default)
    {
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
                SELECT EXISTS (
                    SELECT 1
                    FROM provider_game_mappings
                    WHERE "IsActive" = TRUE
                      AND LOWER(BTRIM("ProviderCode")) = 'mock'
                );
                """;
            var result = await command.ExecuteScalarAsync(cancellationToken);
            if (result is bool hasMockMapping && hasMockMapping)
            {
                throw new InvalidOperationException(
                    "Active AUTO_ID mock provider mappings cannot be enabled in Production.");
            }
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
