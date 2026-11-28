using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Orders;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Catalog;

public sealed record ProductReadinessCheck(string Key, string Label, string Tab, bool Passed, bool Blocking, string Message);
public sealed record ProductReadinessResponse(bool CanPublish, IReadOnlyList<ProductReadinessCheck> Checks);

public sealed class ProductReadinessService(ZetruvDbContext db, AutoIdRuntimeProviderMappingService mappings)
{
    public async Task<Product?> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Products.AsNoTracking().AsSplitQuery()
            .Include(p => p.Variants).Include(p => p.InputFields)
            .SingleOrDefaultAsync(p => p.Id == id, ct);

    public async Task<ProductReadinessResponse> EvaluateAsync(Product p, CancellationToken ct)
    {
        var checks = new List<ProductReadinessCheck>();
        void Add(string key, string label, string tab, bool passed, bool blocking, string issue) =>
            checks.Add(new(key, label, tab, passed, blocking, passed ? "Siap." : issue));

        var category = await db.CatalogCategories.AsNoTracking().SingleOrDefaultAsync(x => x.Id == p.CategoryId, ct);
        var game = p.GameId.HasValue
            ? await db.Games.AsNoTracking().SingleOrDefaultAsync(x => x.Id == p.GameId.Value, ct) : null;
        var requiresGame = p.Kind is ProductKind.TopUpGame or ProductKind.TopUpLogin or ProductKind.GameAccount or ProductKind.GameItem;
        var validInfo = p.Name.Trim().Length is >= 2 and <= 80 &&
            System.Text.RegularExpressions.Regex.IsMatch(p.Name, "[a-zA-Z0-9]") &&
            (p.ShortDescription ?? "").Trim().Length <= 160 &&
            (p.Description ?? "").Trim().Length <= 2000 &&
            (p.Kind != ProductKind.TopUpLogin || (p.ShortDescription ?? "").Trim().Length >= 10);
        Add("information", "Informasi produk", "information", validInfo, true,
            "Periksa nama dan batas karakter deskripsi.");
        Add("category", "Kategori aktif", "information", category is { IsActive: true } && category.Kind == p.Kind, true,
            "Pilih kategori aktif yang sesuai jenis produk.");
        Add("game", "Game terkait", "information", (!requiresGame && !p.GameId.HasValue) || game is { IsActive: true }, true,
            "Pilih game aktif yang sesuai.");
        Add("thumbnail", "Gambar katalog", "images", !string.IsNullOrWhiteSpace(p.ThumbnailUrl) || !string.IsNullOrWhiteSpace(game?.ImageUrl), false,
            "Tambahkan thumbnail produk atau logo game agar kartu katalog memiliki gambar.");
        if (p.Kind != ProductKind.Merchandise)
            Add("mobile", "Background mobile", "images", !string.IsNullOrWhiteSpace(p.HeroMobileUrl), false,
                "Opsional: unggah background mobile untuk tampilan yang lebih sesuai.");

        var variants = p.Variants.Where(v => v.IsActive).ToList();
        Add("variants", "Varian aktif", "variants", variants.Count > 0, true, "Tambahkan minimal satu varian aktif.");
        Add("prices", "Harga dan SKU", "variants", variants.Count > 0 && variants.All(v =>
            v.Price > 0 && v.Price == decimal.Truncate(v.Price) &&
            (!v.CompareAtPrice.HasValue || v.CompareAtPrice >= v.Price) &&
            System.Text.RegularExpressions.Regex.IsMatch(v.Sku.Trim(), "^[A-Za-z0-9][A-Za-z0-9_-]{1,99}$")),
            true, "Pastikan semua varian aktif memiliki harga rupiah positif, harga coret valid, dan SKU valid.");
        bool HasStock(ProductVariant v) => p.Kind switch
        {
            ProductKind.GameAccount => v.StockQuantity == 1,
            ProductKind.GameVoucher or ProductKind.Merchandise => v.StockQuantity > 0,
            _ => v.StockQuantity is null or > 0
        };
        Add("stock", "Stok siap jual", "variants", variants.Any(HasStock), true,
            "Tidak ada varian aktif dengan stok siap jual.");
        if (p.Kind == ProductKind.Merchandise)
            Add("shipping", "Berat dan stok merchandise", "variants", variants.Count > 0 && variants.All(v => v.WeightGrams > 0 && v.StockQuantity.HasValue),
                true, "Isi berat lebih dari 0 gram dan stok eksplisit untuk setiap varian aktif.");
        if (p.Kind == ProductKind.GameVoucher)
        {
            var ids = variants.Where(HasStock).Select(v => v.Id).ToArray();
            var codes = await db.GameVoucherCodes.AsNoTracking().AnyAsync(c =>
                ids.Contains(c.ProductVariantId) && c.Status == GameVoucherCodeStatus.Available && c.EncryptedCode != null, ct);
            Add("voucher", "Inventory kode voucher", "variants", codes, true, "Impor kode voucher yang tersedia pada varian dengan stok siap jual.");
        }
        if (p.FulfillmentMethod is FulfillmentMethod.AUTO_ID or FulfillmentMethod.MANUAL_LOGIN)
        {
            var scope = p.FulfillmentMethod == FulfillmentMethod.AUTO_ID ? ProductInputFieldScope.AccountValidation : ProductInputFieldScope.LoginCredential;
            Add("inputs", "Isian pembeli", "inputs", p.InputFields.Any(f => f.Scope == scope && f.IsRequired), true,
                "Tambahkan minimal satu isian wajib untuk proses pesanan yang dipilih.");
        }
        if (p.FulfillmentMethod == FulfillmentMethod.AUTO_ID)
        {
            var missing = new List<string>();
            foreach (var v in variants)
                if ((await mappings.ResolveAsync(v.Id, ct)).Mapping is null) missing.Add(v.Name);
            Add("provider", "Mapping supplier aktif", "provider", variants.Count > 0 && missing.Count == 0, true,
                missing.Count == 0 ? "Tambahkan varian dan mapping supplier." : "Mapping belum lengkap: " + string.Join(", ", missing));
        }
        if (p.Kind == ProductKind.GameAccount && p.GameId.HasValue)
        {
            var definitions = await db.GameAccountAttributeDefinitions.AsNoTracking().Where(x => x.GameId == p.GameId.Value && x.IsActive).ToListAsync(ct);
            var details = await db.GameAccountDetails.AsNoTracking().SingleOrDefaultAsync(x => x.ProductId == p.Id, ct);
            bool valid = false;
            try
            {
                var values = GameAccountAttributeRules.ParseValues(details?.AttributesJson ?? "{}");
                var activeKeys = definitions.Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
                var activeValues = values.Where(x => activeKeys.Contains(x.Key)).ToDictionary(x => x.Key, x => x.Value);
                valid = GameAccountAttributeRules.NormalizeValues(definitions, activeValues, out _) is null;
            }
            catch (JsonException) { }
            Add("account", "Informasi akun game", "variants", valid, true, "Lengkapi informasi akun yang diwajibkan oleh skema game.");
        }
        return new(checks.All(c => !c.Blocking || c.Passed), checks);
    }
}
