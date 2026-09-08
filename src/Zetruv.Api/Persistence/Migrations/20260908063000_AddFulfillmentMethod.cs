using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Zetruv.Api.Persistence;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations;

[DbContext(typeof(ZetruvDbContext))]
[Migration("20260908063000_AddFulfillmentMethod")]
public partial class AddFulfillmentMethod : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "FulfillmentMethod",
            table: "products",
            type: "character varying(30)",
            maxLength: 30,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "FulfillmentMethod",
            table: "order_items",
            type: "character varying(30)",
            maxLength: 30,
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE products
            SET "FulfillmentMethod" = CASE
                WHEN "Kind" = 'TopUpGame' THEN 'AUTO_ID'
                WHEN "Kind" = 'TopUpLogin' THEN 'MANUAL_LOGIN'
                ELSE 'MANUAL'
            END;
            """);

        migrationBuilder.Sql(
            """
            UPDATE order_items
            SET "FulfillmentMethod" = CASE
                WHEN "ProductKind" = 'TopUpGame' THEN 'AUTO_ID'
                WHEN "ProductKind" = 'TopUpLogin' THEN 'MANUAL_LOGIN'
                ELSE 'MANUAL'
            END;
            """);

        migrationBuilder.AlterColumn<string>(
            name: "FulfillmentMethod",
            table: "products",
            type: "character varying(30)",
            maxLength: 30,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(30)",
            oldMaxLength: 30,
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "FulfillmentMethod",
            table: "order_items",
            type: "character varying(30)",
            maxLength: 30,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(30)",
            oldMaxLength: 30,
            oldNullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "FulfillmentMethod",
            table: "order_items");

        migrationBuilder.DropColumn(
            name: "FulfillmentMethod",
            table: "products");
    }
}
