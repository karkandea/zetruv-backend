using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Zetruv.Api.Persistence;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations;

[DbContext(typeof(ZetruvDbContext))]
[Migration("20260916075500_SeedClientDummyVariants")]
public partial class SeedClientDummyVariants : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            INSERT INTO product_variants
                ("Id", "ProductId", "Name", "Sku", "Price", "CompareAtPrice", "StockQuantity", "WeightGrams", "IsActive", "SortOrder", "CreatedAt", "UpdatedAt")
            SELECT
                seed."Id"::uuid,
                p."Id",
                seed."Name",
                seed."Sku",
                seed."Price",
                NULL,
                NULL,
                NULL,
                TRUE,
                seed."SortOrder",
                NOW(),
                NOW()
            FROM products p
            JOIN (VALUES
                ('d4000000-0000-0000-0000-000000000001', 'efootball-mobile', 'Paket Small',  'DUMMY-EFOOTBALL-S', 10000::numeric, 10),
                ('d4000000-0000-0000-0000-000000000002', 'efootball-mobile', 'Paket Medium', 'DUMMY-EFOOTBALL-M', 25000::numeric, 20),
                ('d4000000-0000-0000-0000-000000000003', 'efootball-mobile', 'Paket Large',  'DUMMY-EFOOTBALL-L', 50000::numeric, 30),
                ('d4000000-0000-0000-0000-000000000004', 'call-of-duty-mobile', 'Paket Small',  'DUMMY-CODM-S', 10000::numeric, 10),
                ('d4000000-0000-0000-0000-000000000005', 'call-of-duty-mobile', 'Paket Medium', 'DUMMY-CODM-M', 25000::numeric, 20),
                ('d4000000-0000-0000-0000-000000000006', 'call-of-duty-mobile', 'Paket Large',  'DUMMY-CODM-L', 50000::numeric, 30)
            ) AS seed("Id", "ProductSlug", "Name", "Sku", "Price", "SortOrder")
                ON p."Slug" = seed."ProductSlug"
            ON CONFLICT ("Sku") DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentionally non-destructive. These DEV seed variants may be edited in CMS
        // or referenced by test orders after the migration has been applied.
    }
}
