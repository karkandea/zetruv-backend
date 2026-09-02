using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Zetruv.Api.Persistence;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations;

[DbContext(typeof(ZetruvDbContext))]
[Migration("20260831082000_AddOrdersAndTransactions")]
public partial class AddOrdersAndTransactions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "orders",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrderNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                PaymentStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                CustomerName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                CustomerEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                CustomerPhone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                ShippingAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                GrandTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                PaymentProvider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                PaymentReference = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_orders", x => x.Id));

        migrationBuilder.CreateTable(
            name: "order_items",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                ProductVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                ProductName = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                ProductSlug = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                ProductKind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                VariantName = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                Sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                ThumbnailUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                GameName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                Quantity = table.Column<int>(type: "integer", nullable: false),
                LineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_order_items", x => x.Id);
                table.ForeignKey(
                    name: "FK_order_items_orders_OrderId",
                    column: x => x.OrderId,
                    principalTable: "orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_order_items_products_ProductId",
                    column: x => x.ProductId,
                    principalTable: "products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_order_items_product_variants_ProductVariantId",
                    column: x => x.ProductVariantId,
                    principalTable: "product_variants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "payment_transactions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                ProviderReference = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payment_transactions", x => x.Id);
                table.ForeignKey(
                    name: "FK_payment_transactions_orders_OrderId",
                    column: x => x.OrderId,
                    principalTable: "orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_orders_OrderNumber",
            table: "orders",
            column: "OrderNumber",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_orders_PaymentReference",
            table: "orders",
            column: "PaymentReference");

        migrationBuilder.CreateIndex(
            name: "IX_orders_Status_PaymentStatus_CreatedAt",
            table: "orders",
            columns: new[] { "Status", "PaymentStatus", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_order_items_OrderId_CreatedAt",
            table: "order_items",
            columns: new[] { "OrderId", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_order_items_ProductId",
            table: "order_items",
            column: "ProductId");

        migrationBuilder.CreateIndex(
            name: "IX_order_items_ProductVariantId",
            table: "order_items",
            column: "ProductVariantId");

        migrationBuilder.CreateIndex(
            name: "IX_payment_transactions_OrderId_CreatedAt",
            table: "payment_transactions",
            columns: new[] { "OrderId", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_payment_transactions_Provider_ProviderReference",
            table: "payment_transactions",
            columns: new[] { "Provider", "ProviderReference" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "payment_transactions");
        migrationBuilder.DropTable(name: "order_items");
        migrationBuilder.DropTable(name: "orders");
    }
}
