using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Zetruv.Api.Persistence;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations;

[DbContext(typeof(ZetruvDbContext))]
[Migration("20260916074000_SeedInitialClientManualLoginCatalog")]
public partial class SeedInitialClientManualLoginCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            INSERT INTO games
                ("Id", "Name", "Slug", "Publisher", "ImageUrl", "IsActive", "IsPopular", "SortOrder", "CreatedAt", "UpdatedAt")
            VALUES
                ('c1000000-0000-0000-0000-000000000001', 'eFootball Mobile', 'efootball-mobile', NULL, NULL, FALSE, FALSE, 20, NOW(), NOW()),
                ('c1000000-0000-0000-0000-000000000002', 'Call of Duty Mobile', 'call-of-duty-mobile', NULL, NULL, FALSE, FALSE, 21, NOW(), NOW())
            ON CONFLICT ("Slug") DO NOTHING;

            INSERT INTO products
                ("Id", "CategoryId", "GameId", "Name", "Slug", "ShortDescription", "Description", "ThumbnailUrl",
                 "Kind", "FulfillmentMethod", "RequiresGameAccountValidation", "IsActive", "IsFeatured", "SortOrder", "CreatedAt", "UpdatedAt")
            SELECT
                'c2000000-0000-0000-0000-000000000001', c."Id", g."Id", 'eFootball Mobile', 'efootball-mobile',
                'Top Up eFootball Mobile via login akun.',
                'Manual login fulfillment. Data login dikirim saat checkout dan diproses oleh admin.',
                NULL, 'TopUpLogin', 'MANUAL_LOGIN', FALSE, FALSE, FALSE, 20, NOW(), NOW()
            FROM catalog_categories c
            JOIN games g ON g."Slug" = 'efootball-mobile'
            WHERE c."Kind" = 'TopUpLogin'
            ORDER BY c."IsActive" DESC, c."SortOrder", c."Name"
            LIMIT 1
            ON CONFLICT ("Slug") DO NOTHING;

            INSERT INTO products
                ("Id", "CategoryId", "GameId", "Name", "Slug", "ShortDescription", "Description", "ThumbnailUrl",
                 "Kind", "FulfillmentMethod", "RequiresGameAccountValidation", "IsActive", "IsFeatured", "SortOrder", "CreatedAt", "UpdatedAt")
            SELECT
                'c2000000-0000-0000-0000-000000000002', c."Id", g."Id", 'Call of Duty Mobile', 'call-of-duty-mobile',
                'Top Up Call of Duty Mobile via login akun.',
                'Manual login fulfillment. Data login dikirim saat checkout dan diproses oleh admin.',
                NULL, 'TopUpLogin', 'MANUAL_LOGIN', FALSE, FALSE, FALSE, 21, NOW(), NOW()
            FROM catalog_categories c
            JOIN games g ON g."Slug" = 'call-of-duty-mobile'
            WHERE c."Kind" = 'TopUpLogin'
            ORDER BY c."IsActive" DESC, c."SortOrder", c."Name"
            LIMIT 1
            ON CONFLICT ("Slug") DO NOTHING;

            INSERT INTO product_input_fields
                ("Id", "ProductId", "Key", "Label", "Scope", "Type", "Placeholder", "HelpText",
                 "IsRequired", "IsSensitive", "MaxLength", "OptionsJson", "SortOrder", "CreatedAt", "UpdatedAt")
            SELECT 'c3000000-0000-0000-0000-000000000001', p."Id", 'email', 'Email Konami', 'LoginCredential', 'Email',
                   'Masukkan Email Konami', 'Email KONAMI yang digunakan untuk login.', TRUE, FALSE, 200, NULL, 10, NOW(), NOW()
            FROM products p WHERE p."Slug" = 'efootball-mobile'
            ON CONFLICT ("ProductId", "Key") DO NOTHING;

            INSERT INTO product_input_fields
                ("Id", "ProductId", "Key", "Label", "Scope", "Type", "Placeholder", "HelpText",
                 "IsRequired", "IsSensitive", "MaxLength", "OptionsJson", "SortOrder", "CreatedAt", "UpdatedAt")
            SELECT 'c3000000-0000-0000-0000-000000000002', p."Id", 'password', 'Password Konami', 'LoginCredential', 'Password',
                   'Masukkan Password Konami', 'Password hanya digunakan untuk proses fulfillment.', TRUE, TRUE, 200, NULL, 20, NOW(), NOW()
            FROM products p WHERE p."Slug" = 'efootball-mobile'
            ON CONFLICT ("ProductId", "Key") DO NOTHING;

            INSERT INTO product_input_fields
                ("Id", "ProductId", "Key", "Label", "Scope", "Type", "Placeholder", "HelpText",
                 "IsRequired", "IsSensitive", "MaxLength", "OptionsJson", "SortOrder", "CreatedAt", "UpdatedAt")
            SELECT 'c3000000-0000-0000-0000-000000000003', p."Id", 'nickname', 'Nickname', 'LoginCredential', 'Text',
                   'Masukkan nickname akun', 'Nickname akun game untuk membantu admin memastikan akun yang benar.', TRUE, FALSE, 120, NULL, 30, NOW(), NOW()
            FROM products p WHERE p."Slug" = 'efootball-mobile'
            ON CONFLICT ("ProductId", "Key") DO NOTHING;

            INSERT INTO product_input_fields
                ("Id", "ProductId", "Key", "Label", "Scope", "Type", "Placeholder", "HelpText",
                 "IsRequired", "IsSensitive", "MaxLength", "OptionsJson", "SortOrder", "CreatedAt", "UpdatedAt")
            SELECT 'c3000000-0000-0000-0000-000000000004', p."Id", 'email', 'Email', 'LoginCredential', 'Email',
                   'Masukkan email akun', 'Email akun Call of Duty Mobile yang digunakan untuk login.', TRUE, FALSE, 200, NULL, 10, NOW(), NOW()
            FROM products p WHERE p."Slug" = 'call-of-duty-mobile'
            ON CONFLICT ("ProductId", "Key") DO NOTHING;

            INSERT INTO product_input_fields
                ("Id", "ProductId", "Key", "Label", "Scope", "Type", "Placeholder", "HelpText",
                 "IsRequired", "IsSensitive", "MaxLength", "OptionsJson", "SortOrder", "CreatedAt", "UpdatedAt")
            SELECT 'c3000000-0000-0000-0000-000000000005', p."Id", 'password', 'Password', 'LoginCredential', 'Password',
                   'Masukkan password akun', 'Password hanya digunakan untuk proses fulfillment.', TRUE, TRUE, 200, NULL, 20, NOW(), NOW()
            FROM products p WHERE p."Slug" = 'call-of-duty-mobile'
            ON CONFLICT ("ProductId", "Key") DO NOTHING;

            INSERT INTO product_input_fields
                ("Id", "ProductId", "Key", "Label", "Scope", "Type", "Placeholder", "HelpText",
                 "IsRequired", "IsSensitive", "MaxLength", "OptionsJson", "SortOrder", "CreatedAt", "UpdatedAt")
            SELECT 'c3000000-0000-0000-0000-000000000006', p."Id", 'nickname', 'Nickname', 'LoginCredential', 'Text',
                   'Masukkan nickname akun', 'Nickname akun game untuk membantu admin memastikan akun yang benar.', TRUE, FALSE, 120, NULL, 30, NOW(), NOW()
            FROM products p WHERE p."Slug" = 'call-of-duty-mobile'
            ON CONFLICT ("ProductId", "Key") DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Client catalog data is intentionally not removed automatically.
        // It may have gained variants, pricing, images, or orders after deployment.
    }
}
