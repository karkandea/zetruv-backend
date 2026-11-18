using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations;

[DbContext(typeof(ZetruvDbContext))]
[Migration("20261118080500_AddGameItemDelivery")]
public sealed class AddGameItemDelivery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "EncryptedGameItemDelivery", table: "order_items", type: "text", nullable: true);
        migrationBuilder.AddColumn<string>(name: "GameItemSupportPhone", table: "site_settings", type: "character varying(16)", maxLength: 16, nullable: true);
        migrationBuilder.Sql("""
            INSERT INTO catalog_categories ("Id", "Key", "Name", "Slug", "Kind", "IsActive", "SortOrder", "CreatedAt", "UpdatedAt")
            VALUES ('39fb56aa-8ee1-479b-87e9-559fcadc0b7e', 'game_items', 'Item Game', 'game-items', 'GameItem', TRUE, 60, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
            ON CONFLICT DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM catalog_categories WHERE "Id" = '39fb56aa-8ee1-479b-87e9-559fcadc0b7e';
            """);
        migrationBuilder.DropColumn(name: "EncryptedGameItemDelivery", table: "order_items");
        migrationBuilder.DropColumn(name: "GameItemSupportPhone", table: "site_settings");
    }
}
