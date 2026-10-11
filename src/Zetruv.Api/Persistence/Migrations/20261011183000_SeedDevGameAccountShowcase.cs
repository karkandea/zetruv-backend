using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Zetruv.Api.Persistence;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations;

// One-time CMS demo catalog for the isolated zetruv_dev database.
// No writes to staging/production databases, and no recurring reseed after CMS edits/deletions.
[DbContext(typeof(ZetruvDbContext))]
[Migration("20261011183000_SeedDevGameAccountShowcase")]
public partial class SeedDevGameAccountShowcase : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $dev_accounts$
            BEGIN
                -- An explicit database guard prevents this data migration from
                -- inserting purchasable demo listings outside the isolated DEV DB.
                IF current_database() <> 'zetruv_dev' THEN
                    RETURN;
                END IF;

                -- Valve's official Steam game art and gameplay screenshots are
                -- illustrations, never evidence of ownership of demo accounts.
                INSERT INTO games
                    ("Id", "Name", "Slug", "Publisher", "ImageUrl", "IsActive", "IsPopular", "SortOrder", "CreatedAt", "UpdatedAt")
                VALUES
                    ('e9100000-0000-0000-0000-000000000001', 'Dota 2', 'dota-2', 'Valve',
                     'https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/570/header.jpg',
                     TRUE, FALSE, 20, NOW(), NOW())
                ON CONFLICT ("Slug") DO NOTHING;

                -- Only add missing CMS field definitions. Existing customized game
                -- schemas, products, and existing CMS listings are never overwritten.
                INSERT INTO game_account_attribute_definitions
                    ("Id", "GameId", "Key", "Label", "Type", "OptionsJson",
                     "IsRequired", "IsActive", "ShowOnCard", "SortOrder", "CreatedAt", "UpdatedAt")
                SELECT
                    seed."Id"::uuid, g."Id", seed."Key", seed."Label", seed."Type",
                    '[]'::jsonb, FALSE, TRUE, seed."ShowOnCard", seed."SortOrder", NOW(), NOW()
                FROM (VALUES
                    ('e9500000-0000-0000-0000-000000000001', 'dota-2', 'rank', 'Rank', 'Text', TRUE, 10),
                    ('e9500000-0000-0000-0000-000000000002', 'dota-2', 'mmr', 'MMR', 'Number', TRUE, 20),
                    ('e9500000-0000-0000-0000-000000000003', 'dota-2', 'region', 'Region', 'Text', TRUE, 30),
                    ('e9500000-0000-0000-0000-000000000004', 'mobile-legends', 'rank', 'Rank', 'Text', TRUE, 10),
                    ('e9500000-0000-0000-0000-000000000005', 'mobile-legends', 'skins', 'Skin', 'Number', TRUE, 20),
                    ('e9500000-0000-0000-0000-000000000006', 'mobile-legends', 'region', 'Region', 'Text', TRUE, 30),
                    ('e9500000-0000-0000-0000-000000000007', 'genshin-impact', 'adventureRank', 'Adventure Rank', 'Number', TRUE, 10),
                    ('e9500000-0000-0000-0000-000000000008', 'genshin-impact', 'characters', 'Karakter', 'Number', TRUE, 20),
                    ('e9500000-0000-0000-0000-000000000009', 'genshin-impact', 'region', 'Server', 'Text', TRUE, 30)
                ) AS seed("Id", "GameSlug", "Key", "Label", "Type", "ShowOnCard", "SortOrder")
                JOIN games g ON g."Slug" = seed."GameSlug"
                ON CONFLICT ("GameId", "Key") DO NOTHING;

                -- Featured slots 1-3 represent three different games; the
                -- fourth Dota listing is accessible from the complete catalog.
                INSERT INTO products
                    ("Id", "CategoryId", "GameId", "Name", "Slug", "ShortDescription", "Description", "ThumbnailUrl",
                     "Kind", "FulfillmentMethod", "RequiresGameAccountValidation",
                     "IsActive", "IsFeatured", "SortOrder", "CreatedAt", "UpdatedAt")
                SELECT
                    seed."Id"::uuid, c."Id", g."Id", seed."Name", seed."Slug",
                    'DATA DEMO DEV - Bukan akun sungguhan. Harga dan atribut hanya untuk pengujian UI.',
                    'Listing demonstrasi khusus lingkungan DEV. Tidak ada akun game sungguhan yang disediakan. Harga, stok, rank, atribut, dan rincian adalah contoh. Foto adalah artwork atau cuplikan gameplay, bukan bukti kepemilikan akun; jangan lakukan pembayaran sungguhan.',
                    seed."ThumbnailUrl",
                    'GameAccount', 'MANUAL', FALSE, TRUE, seed."IsFeatured",
                    seed."SortOrder", NOW(), NOW()
                FROM (VALUES
                    ('e9200000-0000-0000-0000-000000000001', 'dota-2', 'DEMO Dota 2 - Divine V', 'demo-dota2-divine-v',
                     'https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/570/ss_f9ebafedaf2d5cfb80ef1f74baa18eb08cad6494.1920x1080.jpg?t=1731544174',
                     TRUE, 10),
                    ('e9200000-0000-0000-0000-000000000002', 'mobile-legends', 'DEMO MLBB - Mythic 48 Stars', 'demo-mlbb-mythic-48',
                     'https://dev.zetruv.com/assets/search/mobile-legends.webp',
                     TRUE, 20),
                    ('e9200000-0000-0000-0000-000000000003', 'genshin-impact', 'DEMO Genshin Impact - AR 60', 'demo-genshin-ar60',
                     'https://dev.zetruv.com/assets/search/genshin-impact.webp',
                     TRUE, 30),
                    ('e9200000-0000-0000-0000-000000000004', 'dota-2', 'DEMO Dota 2 - Ancient IV', 'demo-dota2-ancient-iv',
                     'https://cdn.cloudflare.steamstatic.com/steam/apps/570/ss_c9118375a2400278590f29a3537769c986ef6e39.1920x1080.jpg?t=1645731580',
                     FALSE, 40)
                ) AS seed("Id", "GameSlug", "Name", "Slug", "ThumbnailUrl", "IsFeatured", "SortOrder")
                JOIN games g ON g."Slug" = seed."GameSlug" AND g."IsActive"
                JOIN catalog_categories c ON c."Kind" = 'GameAccount' AND c."IsActive"
                ORDER BY c."SortOrder", c."Name"
                ON CONFLICT ("Slug") DO NOTHING;

                -- Each fake account is one unique test unit, matching existing
                -- GameAccount validation rules. Mock checkout only in DEV.
                INSERT INTO product_variants
                    ("Id", "ProductId", "Name", "Sku", "Price", "CompareAtPrice",
                     "StockQuantity", "WeightGrams", "IsActive", "SortOrder", "CreatedAt", "UpdatedAt")
                SELECT seed."Id"::uuid, p."Id", '1 Akun DEMO', seed."Sku",
                       seed."Price", NULL, 1, NULL, TRUE, 0, NOW(), NOW()
                FROM (VALUES
                    ('e9300000-0000-0000-0000-000000000001', 'demo-dota2-divine-v', 'ZDEV-DEMO-DOTA-DIVINE', 1450000::numeric),
                    ('e9300000-0000-0000-0000-000000000002', 'demo-mlbb-mythic-48', 'ZDEV-DEMO-ML-MYTHIC', 850000::numeric),
                    ('e9300000-0000-0000-0000-000000000003', 'demo-genshin-ar60', 'ZDEV-DEMO-GENSHIN-AR60', 990000::numeric),
                    ('e9300000-0000-0000-0000-000000000004', 'demo-dota2-ancient-iv', 'ZDEV-DEMO-DOTA-ANCIENT', 1190000::numeric)
                ) AS seed("Id", "ProductSlug", "Sku", "Price")
                JOIN products p ON p."Slug" = seed."ProductSlug" AND p."Kind" = 'GameAccount'
                ON CONFLICT ("Sku") DO NOTHING;

                INSERT INTO game_account_details
                    ("ProductId", "Rank", "SkinCount", "Region", "Level", "AdditionalInfo", "AttributesJson")
                SELECT p."Id", seed."Rank", NULL, seed."Region", NULL,
                    'DATA DEMO DEV - Tidak ada akses akun asli; seluruh spesifikasi hanya ilustrasi.',
                    seed."AttributesJson"::jsonb
                FROM (VALUES
                    ('demo-dota2-divine-v', 'Divine V', 'SEA', '{"rank":"Divine V","mmr":5400,"region":"SEA"}'),
                    ('demo-mlbb-mythic-48', 'Mythic', 'Indonesia', '{"rank":"Mythic 48 Stars","skins":126,"region":"Indonesia"}'),
                    ('demo-genshin-ar60', 'AR 60', 'Asia', '{"adventureRank":60,"characters":48,"region":"Asia"}'),
                    ('demo-dota2-ancient-iv', 'Ancient IV', 'SEA', '{"rank":"Ancient IV","mmr":4200,"region":"SEA"}')
                ) AS seed("ProductSlug", "Rank", "Region", "AttributesJson")
                JOIN products p ON p."Slug" = seed."ProductSlug" AND p."Kind" = 'GameAccount'
                ON CONFLICT ("ProductId") DO NOTHING;

                INSERT INTO product_images ("Id", "ProductId", "Url", "AltText", "SortOrder")
                SELECT seed."Id"::uuid, p."Id", seed."Url", seed."AltText", seed."SortOrder"
                FROM (VALUES
                    ('e9400000-0000-0000-0000-000000000001', 'demo-dota2-divine-v',
                     'https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/570/ss_f9ebafedaf2d5cfb80ef1f74baa18eb08cad6494.1920x1080.jpg?t=1731544174',
                     'Gameplay resmi Dota 2 dari Steam - ilustrasi, bukan bukti kepemilikan akun', 0),
                    ('e9400000-0000-0000-0000-000000000002', 'demo-dota2-divine-v',
                     'https://cdn.cloudflare.steamstatic.com/steam/apps/570/ss_c9118375a2400278590f29a3537769c986ef6e39.1920x1080.jpg?t=1645731580',
                     'Cuplikan gameplay resmi Dota 2 dari Steam - ilustrasi', 1),
                    ('e9400000-0000-0000-0000-000000000003', 'demo-dota2-ancient-iv',
                     'https://cdn.cloudflare.steamstatic.com/steam/apps/570/ss_c9118375a2400278590f29a3537769c986ef6e39.1920x1080.jpg?t=1645731580',
                     'Gameplay resmi Dota 2 dari Steam - ilustrasi, bukan akun asli', 0),
                    ('e9400000-0000-0000-0000-000000000004', 'demo-mlbb-mythic-48',
                     'https://dev.zetruv.com/assets/search/mobile-legends.webp',
                     'Ilustrasi resmi game Mobile Legends - bukan screenshot akun', 0),
                    ('e9400000-0000-0000-0000-000000000005', 'demo-genshin-ar60',
                     'https://dev.zetruv.com/assets/search/genshin-impact.webp',
                     'Ilustrasi game Genshin Impact - bukan screenshot akun', 0)
                ) AS seed("Id", "ProductSlug", "Url", "AltText", "SortOrder")
                JOIN products p ON p."Slug" = seed."ProductSlug" AND p."Kind" = 'GameAccount'
                ON CONFLICT ("Id") DO NOTHING;
            END
            $dev_accounts$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Deliberately leave CMS-owned demo rows alone after creation. They may
        // have been edited or referenced by test orders; no destructive rollback.
    }
}
