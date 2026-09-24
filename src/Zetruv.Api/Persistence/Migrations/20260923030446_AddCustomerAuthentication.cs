using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    EmailVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TokenVersion = table.Column<int>(type: "integer", nullable: false),
                    LastVerificationEmailSentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastPasswordResetEmailSentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "customer_auth_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_auth_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_customer_auth_tokens_customer_users_CustomerUserId",
                        column: x => x.CustomerUserId,
                        principalTable: "customer_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customer_password_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_password_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_customer_password_history_customer_users_CustomerUserId",
                        column: x => x.CustomerUserId,
                        principalTable: "customer_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customer_auth_tokens_CustomerUserId_Purpose",
                table: "customer_auth_tokens",
                columns: new[] { "CustomerUserId", "Purpose" },
                unique: true,
                filter: "\"ConsumedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_customer_auth_tokens_CustomerUserId_Purpose_ExpiresAt",
                table: "customer_auth_tokens",
                columns: new[] { "CustomerUserId", "Purpose", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_auth_tokens_TokenHash",
                table: "customer_auth_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_password_history_CustomerUserId_CreatedAt",
                table: "customer_password_history",
                columns: new[] { "CustomerUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_users_NormalizedEmail",
                table: "customer_users",
                column: "NormalizedEmail",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_auth_tokens");

            migrationBuilder.DropTable(
                name: "customer_password_history");

            migrationBuilder.DropTable(
                name: "customer_users");
        }
    }
}
