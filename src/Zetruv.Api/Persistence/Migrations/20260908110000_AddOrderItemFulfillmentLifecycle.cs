using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Zetruv.Api.Persistence;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations;

[DbContext(typeof(ZetruvDbContext))]
[Migration("20260908110000_AddOrderItemFulfillmentLifecycle")]
public partial class AddOrderItemFulfillmentLifecycle : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "FulfillmentStatus",
            table: "order_items",
            type: "character varying(30)",
            maxLength: 30,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "FulfillmentReference",
            table: "order_items",
            type: "character varying(180)",
            maxLength: 180,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "FulfillmentMessage",
            table: "order_items",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "FulfillmentStartedAt",
            table: "order_items",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "FulfilledAt",
            table: "order_items",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE order_items AS oi
            SET
                "FulfillmentStatus" = CASE
                    WHEN o."Status" = 'Cancelled' THEN 'Cancelled'
                    WHEN o."PaymentStatus" = 'Paid' AND o."Status" = 'Completed' THEN 'Completed'
                    WHEN o."PaymentStatus" = 'Paid' THEN 'Processing'
                    ELSE 'Pending'
                END,
                "FulfillmentStartedAt" = CASE
                    WHEN o."PaymentStatus" = 'Paid' THEN o."PaidAt"
                    ELSE NULL
                END,
                "FulfilledAt" = CASE
                    WHEN o."PaymentStatus" = 'Paid' AND o."Status" = 'Completed'
                        THEN COALESCE(o."CompletedAt", o."PaidAt")
                    ELSE NULL
                END
            FROM orders AS o
            WHERE o."Id" = oi."OrderId";
            """);

        migrationBuilder.AlterColumn<string>(
            name: "FulfillmentStatus",
            table: "order_items",
            type: "character varying(30)",
            maxLength: 30,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(30)",
            oldMaxLength: 30,
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_order_items_OrderId_FulfillmentStatus",
            table: "order_items",
            columns: new[] { "OrderId", "FulfillmentStatus" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_order_items_OrderId_FulfillmentStatus",
            table: "order_items");

        migrationBuilder.DropColumn(
            name: "FulfilledAt",
            table: "order_items");

        migrationBuilder.DropColumn(
            name: "FulfillmentMessage",
            table: "order_items");

        migrationBuilder.DropColumn(
            name: "FulfillmentReference",
            table: "order_items");

        migrationBuilder.DropColumn(
            name: "FulfillmentStartedAt",
            table: "order_items");

        migrationBuilder.DropColumn(
            name: "FulfillmentStatus",
            table: "order_items");
    }
}
