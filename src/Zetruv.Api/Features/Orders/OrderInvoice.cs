using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Orders;

public sealed class OrderInvoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid PaymentTransactionId { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset PaidAt { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public int TemplateVersion { get; set; } = 1;
    public string Status { get; set; } = "Pending";
    public int AttemptCount { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public byte[]? Pdf { get; set; }
    public string? PdfSha256 { get; set; }
}

public sealed record InvoiceInfo(string Number, string Status, DateTimeOffset IssuedAt, bool Available);
public sealed record InvoiceLine(string Name, string? Variant, int Quantity, decimal UnitPrice, decimal Total);
public sealed record InvoiceSnapshot(string Number, string OrderNumber, DateTimeOffset IssuedAt, DateTimeOffset PaidAt,
    string Seller, string? Address, string? Contact, bool Test, string? Customer, string? Email,
    string Currency, decimal Subtotal, decimal Discount, decimal Shipping, decimal Total, string Provider, InvoiceLine[] Items);

public sealed class InvoiceService(ZetruvDbContext db, IConfiguration config, IWebHostEnvironment environment, ILogger<InvoiceService> logger)
{
    public bool Enabled => environment.IsDevelopment() || (config.GetValue<bool>("Invoices:Enabled") &&
        !string.IsNullOrWhiteSpace(config["Invoices:SellerName"]) && !string.IsNullOrWhiteSpace(config["Invoices:SellerAddress"]));
    public bool BackfillEnabled => environment.IsDevelopment() || config.GetValue<bool>("Invoices:BackfillEnabled");
    // Called inside the existing payment transaction, before SaveChanges. No rendering or external IO here.
    public void Issue(Order order, PaymentTransaction payment, DateTimeOffset now)
    {
        if (!Enabled) return;
        if (order.PaidAt is null || payment.Status != PaymentTransactionStatus.Succeeded ||
            payment.Type != PaymentTransactionType.Payment || payment.OrderId != order.Id ||
            payment.Amount != order.GrandTotal || payment.Currency != order.Currency ||
            order.Items.Count == 0 || order.Subtotal - order.DiscountAmount + order.ShippingAmount != order.GrandTotal)
        { logger.LogWarning("Invoice requires review for order {OrderId}", order.Id); return; }
        var lines = order.Items.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(x => new InvoiceLine(x.ProductName, x.VariantName, x.Quantity, x.UnitPrice, x.LineTotal)).ToArray();
        // Item totals are already discounted catalog prices; voucher discount is accounted separately in order total.
        if (lines.Any(x => x.Quantity <= 0 || x.UnitPrice * x.Quantity != x.Total) ||
            lines.Sum(x => x.Total) != order.Subtotal - (order.DiscountAmount - order.VoucherDiscountAmount))
        { logger.LogWarning("Invoice item totals require review for order {OrderId}", order.Id); return; }
        var number = "INV-" + order.OrderNumber;
        var snapshot = new InvoiceSnapshot(number, order.OrderNumber, now, order.PaidAt.Value,
            config["Invoices:SellerName"] ?? "Zetruv", config["Invoices:SellerAddress"], config["Invoices:SellerContact"],
            !environment.IsProduction(), order.CustomerName, order.CustomerEmail, order.Currency, order.Subtotal,
            order.DiscountAmount, order.ShippingAmount, order.GrandTotal, payment.Provider, lines);
        db.Set<OrderInvoice>().Add(new OrderInvoice { OrderId = order.Id, PaymentTransactionId = payment.Id,
            InvoiceNumber = number, IssuedAt = now, PaidAt = order.PaidAt.Value,
            SnapshotJson = JsonSerializer.Serialize(snapshot), NextAttemptAt = now });
    }
    public async Task<InvoiceInfo?> Info(Guid id, CancellationToken ct) => await db.Set<OrderInvoice>().AsNoTracking()
        .Where(x => x.OrderId == id).Select(x => new InvoiceInfo(x.InvoiceNumber, x.Status, x.IssuedAt, x.Status == "Ready")).SingleOrDefaultAsync(ct);

    public async Task Backfill(CancellationToken ct)
    {
        if (!Enabled || !BackfillEnabled) return;
        var ids = await db.Orders.AsNoTracking().Where(x => x.PaymentStatus == PaymentStatus.Paid && x.PaidAt != null &&
            !db.Set<OrderInvoice>().Any(i => i.OrderId == x.Id) &&
            x.Transactions.Count(t => t.Type == PaymentTransactionType.Payment && t.Status == PaymentTransactionStatus.Succeeded &&
                t.Amount == x.GrandTotal && t.Currency == x.Currency) == 1)
            .OrderBy(x => x.CreatedAt).Select(x => x.Id).Take(20).ToListAsync(ct);
        foreach (var id in ids)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Backfill locks the order row, which also serializes with payment's order update.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM orders WHERE \"Id\"={id} FOR UPDATE", ct);
            var order = await db.Orders.AsNoTracking().Include(x => x.Items).Include(x => x.Transactions)
                .SingleAsync(x => x.Id == id, ct);
            if (order.PaymentStatus == PaymentStatus.Paid && !await db.Set<OrderInvoice>().AnyAsync(x => x.OrderId == id, ct))
            {
                var matches = order.Transactions.Where(t => t.Type == PaymentTransactionType.Payment &&
                    t.Status == PaymentTransactionStatus.Succeeded && t.Amount == order.GrandTotal && t.Currency == order.Currency).ToArray();
                if (matches.Length == 1) { Issue(order, matches[0], DateTimeOffset.UtcNow); await db.SaveChangesAsync(ct); }
            }
            await tx.CommitAsync(ct);
            db.ChangeTracker.Clear();
        }
    }

    public async Task RenderNext(CancellationToken ct)
    {
        if (!Enabled) return;
        for (int n = 0; n < 10; n++)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var rows = await db.Set<OrderInvoice>().FromSqlInterpolated($"""
                SELECT * FROM order_invoices WHERE "Status" IN ('Pending','Failed') AND "NextAttemptAt" <= {now}
                ORDER BY "NextAttemptAt" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).ToListAsync(ct);
            var row = rows.SingleOrDefault();
            if (row is null) { await tx.CommitAsync(ct); break; }
            row.AttemptCount++; row.LastAttemptAt = now;
            try
            {
                var snapshot = JsonSerializer.Deserialize<InvoiceSnapshot>(row.SnapshotJson) ?? throw new InvalidOperationException("Invalid invoice snapshot.");
                var bytes = InvoicePdf.Render(snapshot);
                if (bytes.Length > 2_000_000 || bytes.Length < 100) throw new InvalidOperationException("Invalid PDF size.");
                row.Pdf = bytes; row.PdfSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                row.Status = "Ready"; row.NextAttemptAt = null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Invoice render failed for {InvoiceId}", row.Id);
                row.Status = "Failed";
                row.NextAttemptAt = row.AttemptCount >= 5 ? null : now.AddSeconds(15 * Math.Pow(2, row.AttemptCount));
            }
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); db.ChangeTracker.Clear();
        }
    }
}

public sealed class InvoiceWorker(IServiceScopeFactory scopes, ILogger<InvoiceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        do
        {
            try { using var scope = scopes.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<InvoiceService>();
                await service.Backfill(ct); await service.RenderNext(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "Invoice worker iteration failed"); }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
