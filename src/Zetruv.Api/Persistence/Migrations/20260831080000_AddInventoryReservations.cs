using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Zetruv.Api.Persistence;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations;

[DbContext(typeof(ZetruvDbContext))]
[Migration("20260831080000_AddInventoryReservations")]
public partial class AddInventoryReservations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "inventory_reservations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                ProductVariantId = table.Column<Guid>(type: "uuid", nullable: false),
                Quantity = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_inventory_reservations", x => x.Id);
                table.ForeignKey(
                    name: "FK_inventory_reservations_orders_OrderId",
                    column: x => x.OrderId,
                    principalTable: "orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_inventory_reservations_product_variants_ProductVariantId",
                    column: x => x.ProductVariantId,
                    principalTable: "product_variants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_inventory_reservations_OrderId_ProductVariantId",
            table: "inventory_reservations",
            columns: new[] { "OrderId", "ProductVariantId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_inventory_reservations_ProductVariantId",
            table: "inventory_reservations",
            column: "ProductVariantId");

        migrationBuilder.CreateIndex(
            name: "IX_inventory_reservations_Status_ExpiresAt",
            table: "inventory_reservations",
            columns: new[] { "Status", "ExpiresAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "inventory_reservations");
    }
}
