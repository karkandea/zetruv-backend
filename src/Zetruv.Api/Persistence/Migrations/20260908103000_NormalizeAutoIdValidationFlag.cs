using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Zetruv.Api.Persistence;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations;

[DbContext(typeof(ZetruvDbContext))]
[Migration("20260908103000_NormalizeAutoIdValidationFlag")]
public partial class NormalizeAutoIdValidationFlag : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE products
            SET "RequiresGameAccountValidation" =
                CASE WHEN "FulfillmentMethod" = 'AUTO_ID' THEN TRUE ELSE FALSE END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data normalization is intentionally not reversed because the previous
        // boolean values may have represented invalid fulfillment combinations.
    }
}
