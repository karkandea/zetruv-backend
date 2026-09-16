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

            // Legacy pending attempts did not persist their provider-session expiry.
            // Give them the same conservative TTL as the current DEV mock gateway so
            // they cannot become permanently recoverable after this migration.
            migrationBuilder.Sql(
                """
                UPDATE payment_transactions
                SET "ExpiresAt" = "CreatedAt" + INTERVAL '30 minutes'
                WHERE "Type" = 'Payment'
                  AND "Status" = 'Pending'
                  AND "ExpiresAt" IS NULL;

                UPDATE payment_transactions
                SET "PaymentUrl" = 'mock://payment/' || "ProviderReference"
                WHERE "Provider" = 'mock'
                  AND "Type" = 'Payment'
                  AND "Status" = 'Pending'
                  AND "ProviderReference" IS NOT NULL
                  AND "PaymentUrl" IS NULL;
                """);
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
