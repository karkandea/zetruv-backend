using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Zetruv.Api.Persistence;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations;

[DbContext(typeof(ZetruvDbContext))]
[Migration("20260916082000_AddProviderMappings")]
public partial class AddProviderMappings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "provider_game_mappings",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                GameId = table.Column<Guid>(type: "uuid", nullable: false),
                ProviderCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                NicknameCheckEnabled = table.Column<bool>(type: "boolean", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_provider_game_mappings", x => x.Id);
                table.ForeignKey(
                    name: "FK_provider_game_mappings_games_GameId",
                    column: x => x.GameId,
                    principalTable: "games",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "provider_sku_mappings",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProviderGameMappingId = table.Column<Guid>(type: "uuid", nullable: false),
                ProductVariantId = table.Column<Guid>(type: "uuid", nullable: false),
                ProviderSku = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_provider_sku_mappings", x => x.Id);
                table.ForeignKey(
                    name: "FK_provider_sku_mappings_provider_game_mappings_ProviderGameMappingId",
                    column: x => x.ProviderGameMappingId,
                    principalTable: "provider_game_mappings",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_provider_sku_mappings_product_variants_ProductVariantId",
                    column: x => x.ProductVariantId,
                    principalTable: "product_variants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_provider_game_mappings_GameId",
            table: "provider_game_mappings",
            column: "GameId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_provider_game_mappings_IsActive_ProviderCode",
            table: "provider_game_mappings",
            columns: new[] { "IsActive", "ProviderCode" });

        migrationBuilder.CreateIndex(
            name: "IX_provider_sku_mappings_ProductVariantId",
            table: "provider_sku_mappings",
            column: "ProductVariantId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_provider_sku_mappings_ProviderGameMappingId_ProviderSku",
            table: "provider_sku_mappings",
            columns: new[] { "ProviderGameMappingId", "ProviderSku" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_provider_sku_mappings_ProviderGameMappingId_IsActive",
            table: "provider_sku_mappings",
            columns: new[] { "ProviderGameMappingId", "IsActive" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "provider_sku_mappings");
        migrationBuilder.DropTable(name: "provider_game_mappings");
    }
}
