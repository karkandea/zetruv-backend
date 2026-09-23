using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDynamicGameAccountAttributes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttributesJson",
                table: "game_account_details",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.CreateTable(
                name: "game_account_attribute_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OptionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ShowOnCard = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_account_attribute_definitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_game_account_attribute_definitions_games_GameId",
                        column: x => x.GameId,
                        principalTable: "games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // The previous feature revision stored fixed Rank/SkinCount/Region/Level fields.
            // Preserve every existing value before switching public responses to schema-driven JSON.
            migrationBuilder.Sql("""
                UPDATE game_account_details
                SET "AttributesJson" = jsonb_strip_nulls(jsonb_build_object(
                    'rank', NULLIF("Rank", ''),
                    'skinCount', "SkinCount",
                    'region', NULLIF("Region", ''),
                    'level', "Level",
                    'additionalInfo', NULLIF("AdditionalInfo", '')
                ))
                WHERE "AttributesJson" = '{}'::jsonb;
                """);

            // Create editable per-game templates only for games with historical account listings.
            // New games have no forced default fields; each game receives its own CMS schema.
            migrationBuilder.Sql("""
                INSERT INTO game_account_attribute_definitions
                    ("Id", "GameId", "Key", "Label", "Type", "OptionsJson",
                     "IsRequired", "IsActive", "ShowOnCard", "SortOrder", "CreatedAt", "UpdatedAt")
                SELECT gen_random_uuid(), legacy_games."GameId", attrs."Key", attrs."Label",
                       attrs."Type", '[]'::jsonb, FALSE, TRUE,
                       attrs."ShowOnCard", attrs."SortOrder", now(), now()
                FROM (
                    SELECT DISTINCT p."GameId"
                    FROM game_account_details AS d
                    JOIN products AS p ON p."Id" = d."ProductId"
                    WHERE p."GameId" IS NOT NULL
                ) AS legacy_games
                CROSS JOIN (VALUES
                    ('rank', 'Rank', 'Text', TRUE, 0),
                    ('skinCount', 'Skin count', 'Number', TRUE, 10),
                    ('region', 'Region', 'Text', TRUE, 20),
                    ('level', 'Level', 'Number', FALSE, 30),
                    ('additionalInfo', 'Additional info', 'Text', FALSE, 40)
                ) AS attrs("Key", "Label", "Type", "ShowOnCard", "SortOrder");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_game_account_attribute_definitions_GameId_IsActive_SortOrder",
                table: "game_account_attribute_definitions",
                columns: new[] { "GameId", "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_game_account_attribute_definitions_GameId_Key",
                table: "game_account_attribute_definitions",
                columns: new[] { "GameId", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "game_account_attribute_definitions");

            migrationBuilder.DropColumn(
                name: "AttributesJson",
                table: "game_account_details");
        }
    }
}
