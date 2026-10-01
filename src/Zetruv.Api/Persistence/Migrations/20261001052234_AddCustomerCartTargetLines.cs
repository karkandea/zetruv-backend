using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerCartTargetLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_customer_cart_items_CustomerUserId_ProductVariantId",
                table: "customer_cart_items");

            migrationBuilder.AddColumn<Guid>(
                name: "GameAccountValidationId",
                table: "customer_cart_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LineKey",
                table: "customer_cart_items",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE customer_cart_items
                SET "LineKey" =
                    replace("ProductVariantId"::text, '-', '') || ':default';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_customer_cart_items_CustomerUserId_LineKey",
                table: "customer_cart_items",
                columns: new[] { "CustomerUserId", "LineKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_cart_items_GameAccountValidationId",
                table: "customer_cart_items",
                column: "GameAccountValidationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_customer_cart_items_CustomerUserId_LineKey",
                table: "customer_cart_items");

            migrationBuilder.DropIndex(
                name: "IX_customer_cart_items_GameAccountValidationId",
                table: "customer_cart_items");

            migrationBuilder.DropColumn(
                name: "GameAccountValidationId",
                table: "customer_cart_items");

            migrationBuilder.DropColumn(
                name: "LineKey",
                table: "customer_cart_items");

            // Rolling back to the old one-row-per-variant model is destructive
            // when multiple account targets exist. Keep only the newest line.
            migrationBuilder.Sql("""
                DELETE FROM customer_cart_items AS older
                USING customer_cart_items AS newer
                WHERE older."CustomerUserId" = newer."CustomerUserId"
                  AND older."ProductVariantId" = newer."ProductVariantId"
                  AND (
                    older."UpdatedAt" < newer."UpdatedAt"
                    OR (
                      older."UpdatedAt" = newer."UpdatedAt"
                      AND older."Id"::text > newer."Id"::text
                    )
                  );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_customer_cart_items_CustomerUserId_ProductVariantId",
                table: "customer_cart_items",
                columns: new[] { "CustomerUserId", "ProductVariantId" },
                unique: true);
        }
    }
}
