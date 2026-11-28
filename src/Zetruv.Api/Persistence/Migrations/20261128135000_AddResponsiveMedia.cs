using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Zetruv.Api.Persistence;

#nullable disable
namespace Zetruv.Api.Persistence.Migrations;

[DbContext(typeof(ZetruvDbContext))]
[Migration("20261128135000_AddResponsiveMedia")]
public partial class AddResponsiveMedia : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "HeroDesktopUrl", table: "products", type: "character varying(1000)", maxLength: 1000, nullable: true);
        migrationBuilder.AddColumn<string>(name: "HeroMobileUrl", table: "products", type: "character varying(1000)", maxLength: 1000, nullable: true);
        migrationBuilder.AddColumn<string>(name: "MobileImageUrl", table: "home_heroes", type: "character varying(1000)", maxLength: 1000, nullable: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "HeroDesktopUrl", table: "products");
        migrationBuilder.DropColumn(name: "HeroMobileUrl", table: "products");
        migrationBuilder.DropColumn(name: "MobileImageUrl", table: "home_heroes");
    }
}
