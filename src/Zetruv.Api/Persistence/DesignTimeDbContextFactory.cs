using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Zetruv.Api.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ZetruvDbContext>
{
    public ZetruvDbContext CreateDbContext(string[] args)
    {
        // Migrations must use an explicitly configured, private database credential.
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings__Postgres must be set before running EF Core migrations.");
        }

        var options = new DbContextOptionsBuilder<ZetruvDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ZetruvDbContext(options);
    }
}
