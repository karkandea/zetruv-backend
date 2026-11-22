using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations;

[DbContext(typeof(ZetruvDbContext))]
[Migration("20261122133000_AddCustomerProfilesAndAddresses")]
public sealed class AddCustomerProfilesAndAddresses : Migration
{
    protected override void Up(MigrationBuilder b)
    {
        b.AddColumn<string>("Phone", "customer_users", type: "character varying(24)",
            maxLength: 24, nullable: true);
        b.CreateTable("customer_addresses",
            columns: t => new
            {
                Id = t.Column<Guid>(type: "uuid", nullable: false),
                CustomerUserId = t.Column<Guid>(type: "uuid", nullable: false),
                Label = t.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                RecipientName = t.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                RecipientPhone = t.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                Email = t.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                Province = t.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                City = t.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                District = t.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Subdistrict = t.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                PostalCode = t.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                FullAddress = t.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Note = t.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                RajaOngkirDeliveryId = t.Column<int>(type: "integer", nullable: false),
                Latitude = t.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: true),
                Longitude = t.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: true),
                IsDefault = t.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_customer_addresses", x => x.Id);
                t.ForeignKey("FK_customer_addresses_customer_users_CustomerUserId",
                    x => x.CustomerUserId, "customer_users", "Id", onDelete: ReferentialAction.Cascade);
            });
        b.CreateIndex("IX_customer_addresses_CustomerUserId_UpdatedAt",
            "customer_addresses", new[] { "CustomerUserId", "UpdatedAt" });
        b.CreateIndex("IX_customer_addresses_CustomerUserId_IsDefault",
            "customer_addresses", new[] { "CustomerUserId", "IsDefault" },
            unique: true, filter: "\"IsDefault\" = TRUE");
    }
    protected override void Down(MigrationBuilder b)
    {
        b.DropTable("customer_addresses");
        b.DropColumn("Phone", "customer_users");
    }
}
