using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Persistence.Migrations;

[DbContext(typeof(ZetruvDbContext))]
[Migration("20261128100000_AddPurchasedValuePresentation")]
public partial class AddPurchasedValuePresentation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "RankFrom", table: "product_variants", type: "character varying(80)", maxLength: 80, nullable: true);
        migrationBuilder.AddColumn<string>(name: "RankTo", table: "product_variants", type: "character varying(80)", maxLength: 80, nullable: true);
        migrationBuilder.AddColumn<string>(name: "RankFromIconUrl", table: "product_variants", type: "character varying(1000)", maxLength: 1000, nullable: true);
        migrationBuilder.AddColumn<string>(name: "RankToIconUrl", table: "product_variants", type: "character varying(1000)", maxLength: 1000, nullable: true);
        migrationBuilder.AddColumn<string>(name: "VariantIconUrl", table: "order_items", type: "character varying(1000)", maxLength: 1000, nullable: true);
        migrationBuilder.AddColumn<string>(name: "RankFrom", table: "order_items", type: "character varying(80)", maxLength: 80, nullable: true);
        migrationBuilder.AddColumn<string>(name: "RankTo", table: "order_items", type: "character varying(80)", maxLength: 80, nullable: true);
        migrationBuilder.AddColumn<string>(name: "RankFromIconUrl", table: "order_items", type: "character varying(1000)", maxLength: 1000, nullable: true);
        migrationBuilder.AddColumn<string>(name: "RankToIconUrl", table: "order_items", type: "character varying(1000)", maxLength: 1000, nullable: true);
        // Backfill only explicit, unambiguous legacy Joki rank journeys; keep the original variant name.
        migrationBuilder.Sql("""
            UPDATE product_variants v
            SET "RankFrom" = trim(split_part(v."Name", '→', 1)),
                "RankTo" = trim(split_part(v."Name", '→', 2))
            FROM products p
            WHERE p."Id" = v."ProductId" AND p."Kind" = 'Joki'
              AND length(v."Name") - length(replace(v."Name", '→', '')) = 1
              AND length(trim(split_part(v."Name", '→', 1))) BETWEEN 1 AND 80
              AND length(trim(split_part(v."Name", '→', 2))) BETWEEN 1 AND 80;
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "RankFrom", table: "product_variants");
        migrationBuilder.DropColumn(name: "RankTo", table: "product_variants");
        migrationBuilder.DropColumn(name: "RankFromIconUrl", table: "product_variants");
        migrationBuilder.DropColumn(name: "RankToIconUrl", table: "product_variants");
        migrationBuilder.DropColumn(name: "VariantIconUrl", table: "order_items");
        migrationBuilder.DropColumn(name: "RankFrom", table: "order_items");
        migrationBuilder.DropColumn(name: "RankTo", table: "order_items");
        migrationBuilder.DropColumn(name: "RankFromIconUrl", table: "order_items");
        migrationBuilder.DropColumn(name: "RankToIconUrl", table: "order_items");
    }
}
