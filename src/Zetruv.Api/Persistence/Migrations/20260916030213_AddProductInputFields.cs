using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductInputFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_input_fields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Scope = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Placeholder = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    HelpText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    IsSensitive = table.Column<bool>(type: "boolean", nullable: false),
                    MaxLength = table.Column<int>(type: "integer", nullable: false),
                    OptionsJson = table.Column<string>(type: "jsonb", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_input_fields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_input_fields_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_product_input_fields_ProductId_Key",
                table: "product_input_fields",
                columns: new[] { "ProductId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_input_fields_ProductId_Scope_SortOrder",
                table: "product_input_fields",
                columns: new[] { "ProductId", "Scope", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_input_fields");
        }
    }
}
