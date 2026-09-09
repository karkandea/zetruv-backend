using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Zetruv.Api.Persistence;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations;

[DbContext(typeof(ZetruvDbContext))]
[Migration("20260908114500_AddFulfillmentExecutionMetadata")]
public partial class AddFulfillmentExecutionMetadata : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "FulfillmentAttemptCount",
            table: "order_items",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LastFulfillmentAttemptAt",
            table: "order_items",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "FulfillmentAttemptCount",
            table: "order_items");

        migrationBuilder.DropColumn(
            name: "LastFulfillmentAttemptAt",
            table: "order_items");
    }
}
