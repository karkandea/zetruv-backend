using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEncryptedGameVoucherInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "game_voucher_codes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductVariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EncryptedCode = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevealCount = table.Column<int>(type: "integer", nullable: false),
                    LastRevealedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_voucher_codes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_game_voucher_codes_order_items_OrderItemId",
                        column: x => x.OrderItemId,
                        principalTable: "order_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_game_voucher_codes_product_variants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_game_voucher_codes_CodeHash",
                table: "game_voucher_codes",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_game_voucher_codes_OrderItemId_Status",
                table: "game_voucher_codes",
                columns: new[] { "OrderItemId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_game_voucher_codes_ProductVariantId_Status_CreatedAt",
                table: "game_voucher_codes",
                columns: new[] { "ProductVariantId", "Status", "CreatedAt" });

            // Existing GameVoucher stock was manually-entered and had no redeemable-code backing.
            // Fail closed on migration: release any pending reservations without restoring phantom
            // stock, then let CMS code import become the only sellable-stock source.
            migrationBuilder.Sql("""
                UPDATE inventory_reservations AS ir
                SET "Status" = 'Released',
                    "UpdatedAt" = NOW()
                FROM product_variants AS pv
                JOIN products AS p ON p."Id" = pv."ProductId"
                WHERE ir."ProductVariantId" = pv."Id"
                  AND p."Kind" = 'GameVoucher'
                  AND ir."Status" = 'Active';

                UPDATE product_variants AS pv
                SET "StockQuantity" = 0,
                    "UpdatedAt" = NOW()
                FROM products AS p
                WHERE pv."ProductId" = p."Id"
                  AND p."Kind" = 'GameVoucher';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "game_voucher_codes");
        }
    }
}
