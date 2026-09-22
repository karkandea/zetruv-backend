using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentReconciliationMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastReconciliationAttemptAt",
                table: "payment_transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextReconciliationAt",
                table: "payment_transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReconciliationAttemptCount",
                table: "payment_transactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReconciliationMessage",
                table: "payment_transactions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_Status_NextReconciliationAt",
                table: "payment_transactions",
                columns: new[] { "Status", "NextReconciliationAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payment_transactions_Status_NextReconciliationAt",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "LastReconciliationAttemptAt",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "NextReconciliationAt",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "ReconciliationAttemptCount",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "ReconciliationMessage",
                table: "payment_transactions");
        }
    }
}
