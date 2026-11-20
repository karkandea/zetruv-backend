using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations;

[DbContext(typeof(ZetruvDbContext))]
[Migration("20261120072500_AddSupportReports")]
public sealed class AddSupportReports : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "support_reports",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_support_reports", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_support_reports_CreatedAt_Id",
            table: "support_reports",
            columns: new[] { "CreatedAt", "Id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "support_reports");
    }
}
