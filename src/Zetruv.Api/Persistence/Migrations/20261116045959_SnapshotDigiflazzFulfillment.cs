using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations
{
    [DbContext(typeof(ZetruvDbContext))]
    [Migration("20261116045959_SnapshotDigiflazzFulfillment")]
    public partial class SnapshotDigiflazzFulfillment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DigiflazzBuyerBinding",
                table: "order_items",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DigiflazzSku",
                table: "order_items",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DigiflazzBuyerBinding",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "DigiflazzSku",
                table: "order_items");
        }
    }
}
