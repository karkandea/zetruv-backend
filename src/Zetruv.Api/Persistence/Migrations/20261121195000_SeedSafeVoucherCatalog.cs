using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Zetruv.Api.Persistence;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations;

/// <summary>
/// Research-backed starter voucher listings for the DEV catalog, not purchased PINs.
/// Published products are intentionally out of stock until real, encrypted codes
/// are imported by a CMS administrator. The storefront can discover the listings;
/// inventory and payment cannot pretend any code exists.
/// Sources:
///   https://support.google.com/googleplay/answer/3422734?hl=id
///   https://www.unipin.com/id/game/steam-wallet-code-id
///   https://www.unipin.com/id/game/unipin-voucher-id
/// </summary>
[DbContext(typeof(ZetruvDbContext))]
[Migration("20261121195000_SeedSafeVoucherCatalog")]
public partial class SeedSafeVoucherCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            INSERT INTO catalog_categories
                ("Id", "Key", "Name", "Slug", "Description", "Kind", "IsActive", "SortOrder", "CreatedAt", "UpdatedAt")
            SELECT 'd3000000-0000-4000-8000-000000000001'::uuid,
                   'voucher_game', 'Voucher Game', 'voucher-game',
                   'Voucher game dan kode digital', 'GameVoucher', TRUE, 20, NOW(), NOW()
            WHERE NOT EXISTS (SELECT 1 FROM catalog_categories WHERE "Kind" = 'GameVoucher')
            ON CONFLICT ("Slug") DO NOTHING;

            -- Non-destructive publication. If any listing already exists, keep CMS values.
            INSERT INTO products
                ("Id", "CategoryId", "GameId", "Name", "Slug", "ShortDescription",
                 "Description", "ThumbnailUrl", "Kind", "FulfillmentMethod",
                 "RequiresGameAccountValidation", "IsActive", "IsFeatured",
                 "SortOrder", "CreatedAt", "UpdatedAt")
            SELECT 'd4000000-0000-4000-8000-000000000001'::uuid, c."Id", NULL::uuid, 'Google Play Gift Code (Indonesia)', 'google-play-gift-code',
                   'Voucher Google Play untuk akun Indonesia. Kode dikirim setelah pembayaran jika stok tersedia.', 'Voucher digital Google Play regional Indonesia. Hanya berlaku untuk akun Google Play Indonesia. Harga yang tercantum adalah nominal awal, bukan konfirmasi harga jual final. Stok harus diisi menggunakan kode voucher asli lewat CMS sebelum pembelian diaktifkan. Referensi: https://support.google.com/googleplay/answer/3422734?hl=id',
                   '/assets/home/category-voucher.png', 'GameVoucher', 'MANUAL', FALSE,
                   TRUE, FALSE, 10, NOW(), NOW()
            FROM catalog_categories c WHERE c."Kind" = 'GameVoucher'
            ORDER BY c."IsActive" DESC, c."SortOrder" LIMIT 1
            ON CONFLICT ("Slug") DO NOTHING;

            INSERT INTO products
                ("Id", "CategoryId", "GameId", "Name", "Slug", "ShortDescription",
                 "Description", "ThumbnailUrl", "Kind", "FulfillmentMethod",
                 "RequiresGameAccountValidation", "IsActive", "IsFeatured",
                 "SortOrder", "CreatedAt", "UpdatedAt")
            SELECT 'd4000000-0000-4000-8000-000000000002'::uuid, c."Id", NULL::uuid, 'Steam Wallet Code (ID)', 'steam-wallet-idr',
                   'Isi saldo Steam Wallet dengan kode regional Indonesia.', 'Kode Steam Wallet IDR untuk akun dan mata uang yang kompatibel. Nilai nominal awal harus ditinjau kembali oleh admin sebelum stok voucher asli diimpor. Tidak ada kode contoh atau stok fiktif. Referensi: https://www.unipin.com/id/game/steam-wallet-code-id dan https://help.steampowered.com/id/faqs/view/78E3-7431-1E88-AD59',
                   '/assets/home/category-voucher.png', 'GameVoucher', 'MANUAL', FALSE,
                   TRUE, FALSE, 20, NOW(), NOW()
            FROM catalog_categories c WHERE c."Kind" = 'GameVoucher'
            ORDER BY c."IsActive" DESC, c."SortOrder" LIMIT 1
            ON CONFLICT ("Slug") DO NOTHING;

            INSERT INTO products
                ("Id", "CategoryId", "GameId", "Name", "Slug", "ShortDescription",
                 "Description", "ThumbnailUrl", "Kind", "FulfillmentMethod",
                 "RequiresGameAccountValidation", "IsActive", "IsFeatured",
                 "SortOrder", "CreatedAt", "UpdatedAt")
            SELECT 'd4000000-0000-4000-8000-000000000003'::uuid, c."Id", NULL::uuid, 'UniPin Voucher (ID)', 'unipin-voucher-id',
                   'Voucher UniPin untuk menukar saldo atau layanan game yang didukung.', 'Voucher UniPin regional Indonesia. Ketersediaan dan game yang dapat digunakan mengikuti ketentuan UniPin. Harga nominal awal perlu diverifikasi di CMS; hanya impor kode asli yang boleh menaikkan stok. Referensi: https://www.unipin.com/id/game/unipin-voucher-id',
                   '/assets/home/category-voucher.png', 'GameVoucher', 'MANUAL', FALSE,
                   TRUE, FALSE, 30, NOW(), NOW()
            FROM catalog_categories c WHERE c."Kind" = 'GameVoucher'
            ORDER BY c."IsActive" DESC, c."SortOrder" LIMIT 1
            ON CONFLICT ("Slug") DO NOTHING;

            -- Official/publicly documented face values, not verified reseller prices.
            -- StockQuantity=0 is required: only CMS encrypted code imports may
            -- allocate actual sellable inventory. No fake redeem codes are seeded.
            INSERT INTO product_variants
                ("Id", "ProductId", "Name", "Sku", "Price",
                 "StockQuantity", "IsActive", "SortOrder", "CreatedAt", "UpdatedAt")
            SELECT v."Id"::uuid, p."Id", v."Name", v."Sku", v."Price"::numeric,
                   0, TRUE, v."SortOrder", NOW(), NOW()
            FROM (VALUES
                ('d5000000-0000-4000-8000-000000000001', 'google-play-gift-code', 'IDR 5.000', 'GV-GOOGLEPLAYGIFT-5000', 5000, 0),
                ('d5000000-0000-4000-8000-000000000002', 'google-play-gift-code', 'IDR 20.000', 'GV-GOOGLEPLAYGIFT-20000', 20000, 10),
                ('d5000000-0000-4000-8000-000000000003', 'google-play-gift-code', 'IDR 50.000', 'GV-GOOGLEPLAYGIFT-50000', 50000, 20),
                ('d5000000-0000-4000-8000-000000000004', 'google-play-gift-code', 'IDR 100.000', 'GV-GOOGLEPLAYGIFT-100000', 100000, 30),
                ('d5000000-0000-4000-8000-000000000005', 'google-play-gift-code', 'IDR 150.000', 'GV-GOOGLEPLAYGIFT-150000', 150000, 40),
                ('d5000000-0000-4000-8000-000000000006', 'google-play-gift-code', 'IDR 500.000', 'GV-GOOGLEPLAYGIFT-500000', 500000, 50),
                ('d5000000-0000-4000-8000-000000000007', 'steam-wallet-idr', 'IDR 12.000', 'GV-STEAMWALLETIDR-12000', 12000, 0),
                ('d5000000-0000-4000-8000-000000000008', 'steam-wallet-idr', 'IDR 45.000', 'GV-STEAMWALLETIDR-45000', 45000, 10),
                ('d5000000-0000-4000-8000-000000000009', 'unipin-voucher-id', 'IDR 10.000', 'GV-UNIPINVOUCHERI-10000', 10000, 0),
                ('d5000000-0000-4000-8000-000000000010', 'unipin-voucher-id', 'IDR 20.000', 'GV-UNIPINVOUCHERI-20000', 20000, 10),
                ('d5000000-0000-4000-8000-000000000011', 'unipin-voucher-id', 'IDR 50.000', 'GV-UNIPINVOUCHERI-50000', 50000, 20),
                ('d5000000-0000-4000-8000-000000000012', 'unipin-voucher-id', 'IDR 100.000', 'GV-UNIPINVOUCHERI-100000', 100000, 30)
            ) AS v("Id", "ProductSlug", "Name", "Sku", "Price", "SortOrder")
            INNER JOIN products p ON p."Slug" = v."ProductSlug"
            WHERE p."Kind" = 'GameVoucher'
            ON CONFLICT ("Sku") DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Deliberately do not delete catalog or inventory on rollback.
        // CMS may have edited listings, imported real codes or fulfilled orders.
    }
}
