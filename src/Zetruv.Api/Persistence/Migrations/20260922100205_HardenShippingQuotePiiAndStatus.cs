using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenShippingQuotePiiAndStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "RecipientName",
                table: "shipping_quotes",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120);

            migrationBuilder.AlterColumn<string>(
                name: "Province",
                table: "shipping_quotes",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120);

            migrationBuilder.AlterColumn<string>(
                name: "PostalCode",
                table: "shipping_quotes",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10);

            migrationBuilder.AlterColumn<string>(
                name: "Phone",
                table: "shipping_quotes",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "District",
                table: "shipping_quotes",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120);

            migrationBuilder.AlterColumn<string>(
                name: "City",
                table: "shipping_quotes",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120);

            migrationBuilder.AlterColumn<string>(
                name: "AddressLine1",
                table: "shipping_quotes",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(250)",
                oldMaxLength: 250);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PiiClearedAt",
                table: "shipping_quotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """
                ALTER TABLE shipments
                ALTER COLUMN "Status" TYPE character varying(30)
                USING CASE "Status"
                    WHEN 0 THEN 'Pending'
                    WHEN 1 THEN 'ReadyToShip'
                    WHEN 2 THEN 'Shipped'
                    WHEN 3 THEN 'Delivered'
                    WHEN 4 THEN 'Cancelled'
                    ELSE 'Pending'
                END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_shipping_quotes_PiiClearedAt",
                table: "shipping_quotes",
                column: "PiiClearedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_shipping_quotes_PiiClearedAt",
                table: "shipping_quotes");

            migrationBuilder.DropColumn(
                name: "PiiClearedAt",
                table: "shipping_quotes");

            migrationBuilder.Sql(
                """
                UPDATE shipping_quotes
                SET
                    "RecipientName" = COALESCE("RecipientName", ''),
                    "Phone" = COALESCE("Phone", ''),
                    "AddressLine1" = COALESCE("AddressLine1", ''),
                    "District" = COALESCE("District", ''),
                    "City" = COALESCE("City", ''),
                    "Province" = COALESCE("Province", ''),
                    "PostalCode" = COALESCE("PostalCode", '');
                """);

            migrationBuilder.AlterColumn<string>(
                name: "RecipientName",
                table: "shipping_quotes",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Province",
                table: "shipping_quotes",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PostalCode",
                table: "shipping_quotes",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Phone",
                table: "shipping_quotes",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "District",
                table: "shipping_quotes",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "City",
                table: "shipping_quotes",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AddressLine1",
                table: "shipping_quotes",
                type: "character varying(250)",
                maxLength: 250,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(250)",
                oldMaxLength: 250,
                oldNullable: true);

            migrationBuilder.Sql(
                """
                ALTER TABLE shipments
                ALTER COLUMN "Status" TYPE integer
                USING CASE "Status"
                    WHEN 'Pending' THEN 0
                    WHEN 'ReadyToShip' THEN 1
                    WHEN 'Shipped' THEN 2
                    WHEN 'Delivered' THEN 3
                    WHEN 'Cancelled' THEN 4
                    ELSE 0
                END;
                """);
        }
    }
}
