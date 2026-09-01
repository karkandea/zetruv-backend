using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Zetruv.Api.Persistence;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations;

[DbContext(typeof(ZetruvDbContext))]
[Migration("20260901094500_AddGameAccountValidations")]
public partial class AddGameAccountValidations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "game_account_validations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                OrderItemId = table.Column<Guid>(type: "uuid", nullable: true),
                Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                ProviderReference = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                AccountDisplayName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                InputJson = table.Column<string>(type: "jsonb", nullable: false),
                InputFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ValidatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_game_account_validations", x => x.Id);
                table.ForeignKey(
                    name: "FK_game_account_validations_order_items_OrderItemId",
                    column: x => x.OrderItemId,
                    principalTable: "order_items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_game_account_validations_products_ProductId",
                    column: x => x.ProductId,
                    principalTable: "products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_game_account_validations_ExpiresAt",
            table: "game_account_validations",
            column: "ExpiresAt");

        migrationBuilder.CreateIndex(
            name: "IX_game_account_validations_OrderItemId",
            table: "game_account_validations",
            column: "OrderItemId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_game_account_validations_ProductId",
            table: "game_account_validations",
            column: "ProductId");

        migrationBuilder.CreateIndex(
            name: "IX_game_account_validations_Provider_ProviderReference",
            table: "game_account_validations",
            columns: new[] { "Provider", "ProviderReference" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "game_account_validations");
    }
}
