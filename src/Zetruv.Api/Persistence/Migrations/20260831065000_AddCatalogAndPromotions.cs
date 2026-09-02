using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Zetruv.Api.Persistence;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations;

[DbContext(typeof(ZetruvDbContext))]
[Migration("20260831065000_AddCatalogAndPromotions")]
public partial class AddCatalogAndPromotions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "catalog_categories",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                IconUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_catalog_categories", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "games",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                Publisher = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                ImageUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                IsPopular = table.Column<bool>(type: "boolean", nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_games", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "promotions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                Slug = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                IsFlashSale = table.Column<bool>(type: "boolean", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_promotions", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "products",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                GameId = table.Column<Guid>(type: "uuid", nullable: true),
                Name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                Slug = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                ShortDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                Description = table.Column<string>(type: "text", nullable: true),
                ThumbnailUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                RequiresGameAccountValidation = table.Column<bool>(type: "boolean", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                IsFeatured = table.Column<bool>(type: "boolean", nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_products", x => x.Id);
                table.ForeignKey(
                    name: "FK_products_catalog_categories_CategoryId",
                    column: x => x.CategoryId,
                    principalTable: "catalog_categories",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_products_games_GameId",
                    column: x => x.GameId,
                    principalTable: "games",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "product_images",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                AltText = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                SortOrder = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_product_images", x => x.Id);
                table.ForeignKey(
                    name: "FK_product_images_products_ProductId",
                    column: x => x.ProductId,
                    principalTable: "products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "product_variants",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                Sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                CompareAtPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                StockQuantity = table.Column<int>(type: "integer", nullable: true),
                WeightGrams = table.Column<int>(type: "integer", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_product_variants", x => x.Id);
                table.ForeignKey(
                    name: "FK_product_variants_products_ProductId",
                    column: x => x.ProductId,
                    principalTable: "products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "promotion_items",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                ProductVariantId = table.Column<Guid>(type: "uuid", nullable: false),
                SalePrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_promotion_items", x => x.Id);
                table.ForeignKey(
                    name: "FK_promotion_items_product_variants_ProductVariantId",
                    column: x => x.ProductVariantId,
                    principalTable: "product_variants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_promotion_items_promotions_PromotionId",
                    column: x => x.PromotionId,
                    principalTable: "promotions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_catalog_categories_IsActive_SortOrder",
            table: "catalog_categories",
            columns: new[] { "IsActive", "SortOrder" });
        migrationBuilder.CreateIndex(
            name: "IX_catalog_categories_Key",
            table: "catalog_categories",
            column: "Key",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_catalog_categories_Slug",
            table: "catalog_categories",
            column: "Slug",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_games_IsActive_IsPopular_SortOrder",
            table: "games",
            columns: new[] { "IsActive", "IsPopular", "SortOrder" });
        migrationBuilder.CreateIndex(
            name: "IX_games_Slug",
            table: "games",
            column: "Slug",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_products_CategoryId",
            table: "products",
            column: "CategoryId");
        migrationBuilder.CreateIndex(
            name: "IX_products_GameId",
            table: "products",
            column: "GameId");
        migrationBuilder.CreateIndex(
            name: "IX_products_IsActive_Kind_SortOrder",
            table: "products",
            columns: new[] { "IsActive", "Kind", "SortOrder" });
        migrationBuilder.CreateIndex(
            name: "IX_products_Slug",
            table: "products",
            column: "Slug",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_product_images_ProductId_SortOrder",
            table: "product_images",
            columns: new[] { "ProductId", "SortOrder" });

        migrationBuilder.CreateIndex(
            name: "IX_product_variants_ProductId_IsActive_SortOrder",
            table: "product_variants",
            columns: new[] { "ProductId", "IsActive", "SortOrder" });
        migrationBuilder.CreateIndex(
            name: "IX_product_variants_Sku",
            table: "product_variants",
            column: "Sku",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_promotions_IsActive_IsFlashSale_StartsAt_EndsAt",
            table: "promotions",
            columns: new[] { "IsActive", "IsFlashSale", "StartsAt", "EndsAt" });
        migrationBuilder.CreateIndex(
            name: "IX_promotions_Slug",
            table: "promotions",
            column: "Slug",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_promotion_items_ProductVariantId",
            table: "promotion_items",
            column: "ProductVariantId");
        migrationBuilder.CreateIndex(
            name: "IX_promotion_items_PromotionId_ProductVariantId",
            table: "promotion_items",
            columns: new[] { "PromotionId", "ProductVariantId" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_promotion_items_PromotionId_SortOrder",
            table: "promotion_items",
            columns: new[] { "PromotionId", "SortOrder" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "product_images");
        migrationBuilder.DropTable(name: "promotion_items");
        migrationBuilder.DropTable(name: "product_variants");
        migrationBuilder.DropTable(name: "promotions");
        migrationBuilder.DropTable(name: "products");
        migrationBuilder.DropTable(name: "catalog_categories");
        migrationBuilder.DropTable(name: "games");
    }
}
