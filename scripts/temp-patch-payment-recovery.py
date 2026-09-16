from pathlib import Path

# PaymentTransaction recovery metadata.
path = Path('src/Zetruv.Api/Features/Orders/OrderModels.cs')
text = path.read_text()
old = '''    public decimal Amount { get; set; }\n    public string Currency { get; set; } = "IDR";\n    public DateTimeOffset? ProcessedAt { get; set; }\n'''
new = '''    public decimal Amount { get; set; }\n    public string Currency { get; set; } = "IDR";\n    public string? PaymentUrl { get; set; }\n    public string? QrString { get; set; }\n    public DateTimeOffset? ExpiresAt { get; set; }\n    public DateTimeOffset? ProcessedAt { get; set; }\n'''
if old not in text:
    raise SystemExit('OrderModels PaymentTransaction anchor not found')
path.write_text(text.replace(old, new, 1))

# EF mapping for new recovery fields.
path = Path('src/Zetruv.Api/Persistence/ZetruvDbContext.cs')
text = path.read_text()
old = '''            entity.Property(x => x.Amount).HasPrecision(18, 2);\n            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();\n            entity.HasIndex(x => new { x.OrderId, x.CreatedAt });\n'''
new = '''            entity.Property(x => x.Amount).HasPrecision(18, 2);\n            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();\n            entity.Property(x => x.PaymentUrl).HasMaxLength(2000);\n            entity.Property(x => x.QrString).HasColumnType("text");\n            entity.HasIndex(x => new { x.OrderId, x.CreatedAt });\n'''
if old not in text:
    raise SystemExit('DbContext PaymentTransaction anchor not found')
path.write_text(text.replace(old, new, 1))

# Order lookup exposes enough state for a consumer to distinguish an existing
# recoverable payment session from a new retry attempt, without exposing the
# payment URL itself before the order access token is used.
path = Path('src/Zetruv.Api/Features/Orders/OrderTracking.cs')
text = path.read_text()
old = '''    string Currency,\n    bool CanInitiatePayment,\n    string? OrderAccessToken,\n'''
new = '''    string Currency,\n    bool CanInitiatePayment,\n    bool HasActivePaymentSession,\n    DateTimeOffset? ActivePaymentExpiresAt,\n    string? OrderAccessToken,\n'''
if old not in text:
    raise SystemExit('TrackOrderResponse anchor not found')
text = text.replace(old, new, 1)

old = '''        var order = await db.Orders\n            .AsNoTracking()\n'''
new = '''        var now = DateTimeOffset.UtcNow;\n        var order = await db.Orders\n            .AsNoTracking()\n'''
if old not in text:
    raise SystemExit('TrackAsync query anchor not found')
text = text.replace(old, new, 1)

old = '''                x.Status != OrderStatus.Cancelled &&\n                    (x.PaymentStatus == PaymentStatus.Pending || x.PaymentStatus == PaymentStatus.Failed),\n                null,\n                null,\n                x.CreatedAt,\n'''
new = '''                x.Status != OrderStatus.Cancelled &&\n                    (x.PaymentStatus == PaymentStatus.Pending || x.PaymentStatus == PaymentStatus.Failed),\n                x.Transactions.Any(t =>\n                    t.Type == PaymentTransactionType.Payment &&\n                    t.Status == PaymentTransactionStatus.Pending &&\n                    (!t.ExpiresAt.HasValue || t.ExpiresAt > now)),\n                x.Transactions\n                    .Where(t =>\n                        t.Type == PaymentTransactionType.Payment &&\n                        t.Status == PaymentTransactionStatus.Pending &&\n                        (!t.ExpiresAt.HasValue || t.ExpiresAt > now))\n                    .OrderByDescending(t => t.CreatedAt)\n                    .Select(t => t.ExpiresAt)\n                    .FirstOrDefault(),\n                null,\n                null,\n                x.CreatedAt,\n'''
if old not in text:
    raise SystemExit('TrackOrderResponse projection anchor not found')
text = text.replace(old, new, 1)
path.write_text(text)

print('payment recovery source patches applied')
