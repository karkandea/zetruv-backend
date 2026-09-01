using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Zetruv.Api.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ZetruvDbContext>
{
    public ZetruvDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=127.0.0.1;Port=5432;Database=zetruv_design;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<ZetruvDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ZetruvDbContext(options);
    }
}
