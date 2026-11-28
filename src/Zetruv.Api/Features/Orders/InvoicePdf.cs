using System.Globalization;
using SkiaSharp;

namespace Zetruv.Api.Features.Orders;

public static class InvoicePdf
{
    public static byte[] Render(InvoiceSnapshot invoice)
    {
        using var output = new MemoryStream();
        using var doc = SKDocument.CreatePdf(output) ?? throw new InvalidOperationException("PDF unavailable");
        using var face = SKTypeface.FromFile(Path.Combine(AppContext.BaseDirectory, "InvoiceAssets", "DejaVuSans.ttf")) ?? throw new InvalidOperationException("Invoice font unavailable");
        using var bold = SKTypeface.FromFile(Path.Combine(AppContext.BaseDirectory, "InvoiceAssets", "DejaVuSans-Bold.ttf")) ?? throw new InvalidOperationException("Invoice font unavailable");
        using var regular = new SKFont(face, 10);
        using var strong = new SKFont(bold, 10);
        using var title = new SKFont(bold, 24);
        using var paint = new SKPaint { IsAntialias = true };
        var navy = SKColor.Parse("#15154F");
        var muted = SKColor.Parse("#657083");
        var ink = SKColor.Parse("#20283A");
        var border = SKColor.Parse("#DEE3EB");
        var culture = CultureInfo.GetCultureInfo("id-ID");
        string Money(decimal n) => (invoice.Currency == "IDR" ? "Rp" : invoice.Currency + " ") + n.ToString(n == decimal.Truncate(n) ? "N0" : "N2", culture);
        string Date(DateTimeOffset d) => d.ToOffset(TimeSpan.FromHours(7)).ToString("dd MMM yyyy, HH:mm", culture) + " WIB";
        SKCanvas canvas = null!;
        float y = 0;
        int page = 0;
        void Text(string? value, float x, float baseline, SKFont font, SKColor color, bool right = false)
        {
            if (string.IsNullOrEmpty(value)) return;
            paint.Color = color;
            canvas.DrawText(value, right ? x - font.MeasureText(value) : x, baseline, SKTextAlign.Left, font, paint);
        }
        void Rule(float at)
        {
            paint.Color = border; paint.StrokeWidth = 0.7f; canvas.DrawLine(42, at, 553, at, paint);
        }
        IEnumerable<string> Wrap(string? value, SKFont font, float width)
        {
            if (string.IsNullOrWhiteSpace(value)) yield break;
            var current = "";
            foreach (var word in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (font.MeasureText(candidate) <= width) { current = candidate; continue; }
                if (current.Length > 0) { yield return current; current = ""; }
                foreach (var rune in word.EnumerateRunes())
                {
                    var next = current + rune.ToString();
                    if (current.Length > 0 && font.MeasureText(next) > width) { yield return current; current = rune.ToString(); }
                    else current = next;
                }
            }
            if (current.Length > 0) yield return current;
        }
        void EndPage()
        {
            Rule(790); Text(invoice.Test ? "Dokumen sandbox - bukan bukti pembayaran aktual" : "Terima kasih telah bertransaksi di Zetruv.", 42, 807, regular, muted);
            Text("Halaman " + page, 553, 807, regular, muted, true);
            doc.EndPage();
        }
        void Header()
        {
            canvas = doc.BeginPage(595, 842); page++;
            canvas.Clear(SKColors.White);
            Text("ZETRUV", 42, 66, title, navy);
            Text("INVOICE", 553, 66, title, navy, true);
            Text(invoice.Number, 553, 87, strong, ink, true);
            Text(invoice.Test ? "SANDBOX / UJI COBA" : "LUNAS", 553, 104, strong, invoice.Test ? muted : SKColor.Parse("#167548"), true);
            Rule(119); y = 140;
        }
        void TableHeader()
        {
            paint.Color = SKColor.Parse("#EFF2F8"); canvas.DrawRect(42, y, 511, 26, paint);
            Text("Produk / Varian", 50, y + 17, strong, navy);
            Text("Qty", 325, y + 17, strong, navy, true);
            Text("Harga", 432, y + 17, strong, navy, true);
            Text("Jumlah", 545, y + 17, strong, navy, true);
            y += 42;
        }
        void Ensure(float height, bool table = false)
        {
            if (y + height <= 760) return;
            EndPage(); Header(); if (table) TableHeader();
        }
        void Info(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            foreach (var line in Wrap(label + ": " + value, regular, 511)) { Ensure(16); Text(line, 42, y, regular, ink); y += 16; }
        }
        Header();
        Info("Penjual", invoice.Seller); Info("Alamat", invoice.Address); Info("Kontak", invoice.Contact);
        Info("Pelanggan", invoice.Customer); Info("Email", invoice.Email);
        Info("Pesanan", invoice.OrderNumber); Info("Tanggal invoice", Date(invoice.IssuedAt));
        Info("Dibayar", Date(invoice.PaidAt)); Info("Provider pembayaran", invoice.Provider);
        y += 10; Ensure(50); TableHeader();
        foreach (var item in invoice.Items)
        {
            var lines = Wrap(item.Name, strong, 253).Select(x => (x, true))
                .Concat(Wrap(item.Variant, regular, 253).Select(x => (x, false))).ToArray();
            if (lines.Length == 0) lines = [("Produk", true)];
            Ensure(30, true);
            Text(item.Quantity.ToString(culture), 325, y, regular, ink, true);
            Text(Money(item.UnitPrice), 432, y, regular, ink, true);
            Text(Money(item.Total), 545, y, strong, ink, true);
            foreach (var (line, isBold) in lines)
            {
                Ensure(18, true); Text(line, 50, y, isBold ? strong : regular, isBold ? ink : muted); y += 15;
            }
            Rule(y - 4); y += 10;
        }
        Ensure(140); y += 10;
        void Total(string label, decimal amount, bool prominent = false)
        {
            Text(label, 295, y, prominent ? strong : regular, prominent ? navy : muted);
            Text(Money(amount), 545, y, strong, prominent ? navy : ink, true); y += 20;
        }
        Total("Subtotal (harga awal)", invoice.Subtotal);
        if (invoice.Discount > 0) Total("Total diskon", -invoice.Discount);
        if (invoice.Shipping > 0) Total("Ongkir", invoice.Shipping);
        Rule(y - 8); y += 9; Total("Total dibayar", invoice.Total, true);
        Text("Harga item ditampilkan setelah promo produk.", 42, y + 9, regular, muted);
        EndPage(); doc.Close();
        return output.ToArray();
    }
}
