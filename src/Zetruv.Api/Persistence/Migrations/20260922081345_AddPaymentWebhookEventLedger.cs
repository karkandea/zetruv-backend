using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentWebhookEventLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_webhook_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ProviderEventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    EventFingerprintSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WebhookStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DeliveryCount = table.Column<int>(type: "integer", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResultMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsConfigurationError = table.Column<bool>(type: "boolean", nullable: false),
                    IsNotFound = table.Column<bool>(type: "boolean", nullable: false),
                    IsConflict = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_webhook_events", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payment_webhook_events_OrderId_ReceivedAt",
                table: "payment_webhook_events",
                columns: new[] { "OrderId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_webhook_events_Provider_ProviderEventId",
                table: "payment_webhook_events",
                columns: new[] { "Provider", "ProviderEventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_webhook_events");
        }
    }
}
