using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStorefrontPersonalizationAndMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CustomerUserId",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "customer_cart_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductVariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_cart_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_customer_cart_items_customer_users_CustomerUserId",
                        column: x => x.CustomerUserId,
                        principalTable: "customer_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_customer_cart_items_product_variants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "game_account_details",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rank = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SkinCount = table.Column<int>(type: "integer", nullable: true),
                    Region = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: true),
                    AdditionalInfo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_account_details", x => x.ProductId);
                    table.ForeignKey(
                        name: "FK_game_account_details_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsApproved = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_reviews_customer_users_CustomerUserId",
                        column: x => x.CustomerUserId,
                        principalTable: "customer_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_product_reviews_order_items_OrderItemId",
                        column: x => x.OrderItemId,
                        principalTable: "order_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_product_reviews_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_orders_CustomerUserId_PaidAt",
                table: "orders",
                columns: new[] { "CustomerUserId", "PaidAt" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_cart_items_CustomerUserId_ProductVariantId",
                table: "customer_cart_items",
                columns: new[] { "CustomerUserId", "ProductVariantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_cart_items_ProductVariantId",
                table: "customer_cart_items",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_product_reviews_CustomerUserId",
                table: "product_reviews",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_product_reviews_OrderItemId",
                table: "product_reviews",
                column: "OrderItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_reviews_ProductId_IsApproved",
                table: "product_reviews",
                columns: new[] { "ProductId", "IsApproved" });

            migrationBuilder.AddForeignKey(
                name: "FK_orders_customer_users_CustomerUserId",
                table: "orders",
                column: "CustomerUserId",
                principalTable: "customer_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_orders_customer_users_CustomerUserId",
                table: "orders");

            migrationBuilder.DropTable(
                name: "customer_cart_items");

            migrationBuilder.DropTable(
                name: "game_account_details");

            migrationBuilder.DropTable(
                name: "product_reviews");

            migrationBuilder.DropIndex(
                name: "IX_orders_CustomerUserId_PaidAt",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "CustomerUserId",
                table: "orders");
        }
    }
}
