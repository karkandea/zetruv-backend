using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentRecoveryMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "payment_transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentUrl",
                table: "payment_transactions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QrString",
                table: "payment_transactions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "PaymentUrl",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "QrString",
                table: "payment_transactions");
        }
    }
}
