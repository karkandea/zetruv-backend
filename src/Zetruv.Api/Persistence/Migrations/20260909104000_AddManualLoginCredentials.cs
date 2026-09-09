using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Zetruv.Api.Persistence;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations;

[DbContext(typeof(ZetruvDbContext))]
[Migration("20260909104000_AddManualLoginCredentials")]
public partial class AddManualLoginCredentials : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "manual_login_credentials",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrderItemId = table.Column<Guid>(type: "uuid", nullable: false),
                EncryptedPayload = table.Column<string>(type: "text", nullable: true),
                FieldNamesJson = table.Column<string>(type: "jsonb", nullable: false),
                RevealCount = table.Column<int>(type: "integer", nullable: false),
                LastRevealedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ClearedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_manual_login_credentials", x => x.Id);
                table.ForeignKey(
                    name: "FK_manual_login_credentials_order_items_OrderItemId",
                    column: x => x.OrderItemId,
                    principalTable: "order_items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_manual_login_credentials_OrderItemId",
            table: "manual_login_credentials",
            column: "OrderItemId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "manual_login_credentials");
    }
}
