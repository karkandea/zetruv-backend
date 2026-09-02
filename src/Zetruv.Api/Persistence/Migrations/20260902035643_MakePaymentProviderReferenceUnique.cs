using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakePaymentProviderReferenceUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payment_transactions_Provider_ProviderReference",
                table: "payment_transactions");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_Provider_ProviderReference",
                table: "payment_transactions",
                columns: new[] { "Provider", "ProviderReference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payment_transactions_Provider_ProviderReference",
                table: "payment_transactions");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_Provider_ProviderReference",
                table: "payment_transactions",
                columns: new[] { "Provider", "ProviderReference" });
        }
    }
}
