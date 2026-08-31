using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations;

[DbContext(typeof(ZetruvDbContext))]
partial class ZetruvDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.11")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        modelBuilder.Entity("Zetruv.Api.Features.Auth.AdminUser", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("Email").IsRequired().HasMaxLength(320).HasColumnType("character varying(320)");
            b.Property<bool>("IsActive").HasColumnType("boolean");
            b.Property<string>("NormalizedEmail").IsRequired().HasMaxLength(320).HasColumnType("character varying(320)");
            b.Property<string>("PasswordHash").IsRequired().HasMaxLength(1000).HasColumnType("character varying(1000)");
            b.Property<string>("Role").IsRequired().HasMaxLength(50).HasColumnType("character varying(50)");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("NormalizedEmail").IsUnique();
            b.ToTable("admin_users");
        });

        modelBuilder.Entity("Zetruv.Api.Features.Articles.ArticleCategory", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<bool>("IsActive").HasColumnType("boolean");
            b.Property<string>("Name").IsRequired().HasMaxLength(120).HasColumnType("character varying(120)");
            b.Property<string>("Slug").IsRequired().HasMaxLength(160).HasColumnType("character varying(160)");
            b.Property<int>("SortOrder").HasColumnType("integer");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("IsActive", "SortOrder");
            b.HasIndex("Slug").IsUnique();
            b.ToTable("article_categories");
        });

        modelBuilder.Entity("Zetruv.Api.Features.Articles.Article", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<string>("AuthorName").HasMaxLength(120).HasColumnType("character varying(120)");
            b.Property<Guid>("CategoryId").HasColumnType("uuid");
            b.Property<string>("Content").IsRequired().HasColumnType("text");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("Excerpt").IsRequired().HasMaxLength(600).HasColumnType("character varying(600)");
            b.Property<bool>("IsFeatured").HasColumnType("boolean");
            b.Property<bool>("IsPublished").HasColumnType("boolean");
            b.Property<DateTimeOffset?>("PublishedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("Slug").IsRequired().HasMaxLength(240).HasColumnType("character varying(240)");
            b.Property<string>("ThumbnailUrl").IsRequired().HasMaxLength(1000).HasColumnType("character varying(1000)");
            b.Property<string>("Title").IsRequired().HasMaxLength(220).HasColumnType("character varying(220)");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("CategoryId");
            b.HasIndex("IsPublished", "PublishedAt");
            b.HasIndex("Slug").IsUnique();
            b.ToTable("articles");
        });

        modelBuilder.Entity("Zetruv.Api.Features.Catalog.CatalogCategory", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("Description").HasMaxLength(500).HasColumnType("character varying(500)");
            b.Property<string>("IconUrl").HasMaxLength(1000).HasColumnType("character varying(1000)");
            b.Property<bool>("IsActive").HasColumnType("boolean");
            b.Property<string>("Key").IsRequired().HasMaxLength(80).HasColumnType("character varying(80)");
            b.Property<string>("Kind").IsRequired().HasMaxLength(30).HasColumnType("character varying(30)");
            b.Property<string>("Name").IsRequired().HasMaxLength(120).HasColumnType("character varying(120)");
            b.Property<string>("Slug").IsRequired().HasMaxLength(160).HasColumnType("character varying(160)");
            b.Property<int>("SortOrder").HasColumnType("integer");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("IsActive", "SortOrder");
            b.HasIndex("Key").IsUnique();
            b.HasIndex("Slug").IsUnique();
            b.ToTable("catalog_categories");
        });

        modelBuilder.Entity("Zetruv.Api.Features.Catalog.Game", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("ImageUrl").HasMaxLength(1000).HasColumnType("character varying(1000)");
            b.Property<bool>("IsActive").HasColumnType("boolean");
            b.Property<bool>("IsPopular").HasColumnType("boolean");
            b.Property<string>("Name").IsRequired().HasMaxLength(120).HasColumnType("character varying(120)");
            b.Property<string>("Publisher").HasMaxLength(120).HasColumnType("character varying(120)");
            b.Property<string>("Slug").IsRequired().HasMaxLength(160).HasColumnType("character varying(160)");
            b.Property<int>("SortOrder").HasColumnType("integer");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("IsActive", "IsPopular", "SortOrder");
            b.HasIndex("Slug").IsUnique();
            b.ToTable("games");
        });

        modelBuilder.Entity("Zetruv.Api.Features.Catalog.Product", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<Guid>("CategoryId").HasColumnType("uuid");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("Description").HasColumnType("text");
            b.Property<Guid?>("GameId").HasColumnType("uuid");
            b.Property<bool>("IsActive").HasColumnType("boolean");
            b.Property<bool>("IsFeatured").HasColumnType("boolean");
            b.Property<string>("Kind").IsRequired().HasMaxLength(30).HasColumnType("character varying(30)");
            b.Property<string>("Name").IsRequired().HasMaxLength(180).HasColumnType("character varying(180)");
            b.Property<bool>("RequiresGameAccountValidation").HasColumnType("boolean");
            b.Property<string>("ShortDescription").HasMaxLength(500).HasColumnType("character varying(500)");
            b.Property<string>("Slug").IsRequired().HasMaxLength(220).HasColumnType("character varying(220)");
            b.Property<int>("SortOrder").HasColumnType("integer");
            b.Property<string>("ThumbnailUrl").HasMaxLength(1000).HasColumnType("character varying(1000)");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("CategoryId");
            b.HasIndex("GameId");
            b.HasIndex("IsActive", "Kind", "SortOrder");
            b.HasIndex("Slug").IsUnique();
            b.ToTable("products");
        });

        modelBuilder.Entity("Zetruv.Api.Features.Catalog.ProductImage", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<string>("AltText").HasMaxLength(250).HasColumnType("character varying(250)");
            b.Property<Guid>("ProductId").HasColumnType("uuid");
            b.Property<int>("SortOrder").HasColumnType("integer");
            b.Property<string>("Url").IsRequired().HasMaxLength(1000).HasColumnType("character varying(1000)");
            b.HasKey("Id");
            b.HasIndex("ProductId", "SortOrder");
            b.ToTable("product_images");
        });

        modelBuilder.Entity("Zetruv.Api.Features.Catalog.ProductVariant", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<decimal?>("CompareAtPrice").HasPrecision(18, 2).HasColumnType("numeric(18,2)");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<bool>("IsActive").HasColumnType("boolean");
            b.Property<string>("Name").IsRequired().HasMaxLength(180).HasColumnType("character varying(180)");
            b.Property<decimal>("Price").HasPrecision(18, 2).HasColumnType("numeric(18,2)");
            b.Property<Guid>("ProductId").HasColumnType("uuid");
            b.Property<string>("Sku").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<int>("SortOrder").HasColumnType("integer");
            b.Property<int?>("StockQuantity").HasColumnType("integer");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.Property<int?>("WeightGrams").HasColumnType("integer");
            b.HasKey("Id");
            b.HasIndex("ProductId", "IsActive", "SortOrder");
            b.HasIndex("Sku").IsUnique();
            b.ToTable("product_variants");
        });

        modelBuilder.Entity("Zetruv.Api.Features.Catalog.Promotion", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset>("EndsAt").HasColumnType("timestamp with time zone");
            b.Property<bool>("IsActive").HasColumnType("boolean");
            b.Property<bool>("IsFlashSale").HasColumnType("boolean");
            b.Property<string>("Name").IsRequired().HasMaxLength(160).HasColumnType("character varying(160)");
            b.Property<string>("Slug").IsRequired().HasMaxLength(180).HasColumnType("character varying(180)");
            b.Property<DateTimeOffset>("StartsAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("IsActive", "IsFlashSale", "StartsAt", "EndsAt");
            b.HasIndex("Slug").IsUnique();
            b.ToTable("promotions");
        });

        modelBuilder.Entity("Zetruv.Api.Features.Catalog.PromotionItem", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<Guid>("ProductVariantId").HasColumnType("uuid");
            b.Property<Guid>("PromotionId").HasColumnType("uuid");
            b.Property<decimal>("SalePrice").HasPrecision(18, 2).HasColumnType("numeric(18,2)");
            b.Property<int>("SortOrder").HasColumnType("integer");
            b.HasKey("Id");
            b.HasIndex("ProductVariantId");
            b.HasIndex("PromotionId", "ProductVariantId").IsUnique();
            b.HasIndex("PromotionId", "SortOrder");
            b.ToTable("promotion_items");
        });

        modelBuilder.Entity("Zetruv.Api.Features.Home.HomeHero", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset?>("EndsAt").HasColumnType("timestamp with time zone");
            b.Property<string>("ImageUrl").IsRequired().HasMaxLength(1000).HasColumnType("character varying(1000)");
            b.Property<bool>("IsActive").HasColumnType("boolean");
            b.Property<string>("PrimaryCtaLabel").HasMaxLength(80).HasColumnType("character varying(80)");
            b.Property<string>("PrimaryCtaUrl").HasMaxLength(500).HasColumnType("character varying(500)");
            b.Property<string>("SecondaryCtaLabel").HasMaxLength(80).HasColumnType("character varying(80)");
            b.Property<string>("SecondaryCtaUrl").HasMaxLength(500).HasColumnType("character varying(500)");
            b.Property<int>("SortOrder").HasColumnType("integer");
            b.Property<DateTimeOffset?>("StartsAt").HasColumnType("timestamp with time zone");
            b.Property<string>("Subtitle").IsRequired().HasMaxLength(500).HasColumnType("character varying(500)");
            b.Property<string>("Title").IsRequired().HasMaxLength(160).HasColumnType("character varying(160)");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("IsActive", "SortOrder");
            b.ToTable("home_heroes");
        });

        modelBuilder.Entity("Zetruv.Api.Features.Home.HomeSection", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("CtaLabel").HasMaxLength(80).HasColumnType("character varying(80)");
            b.Property<string>("CtaUrl").HasMaxLength(500).HasColumnType("character varying(500)");
            b.Property<bool>("IsEnabled").HasColumnType("boolean");
            b.Property<int>("ItemLimit").HasColumnType("integer");
            b.Property<string>("Key").IsRequired().HasMaxLength(50).HasColumnType("character varying(50)");
            b.Property<int>("SortOrder").HasColumnType("integer");
            b.Property<string>("Subtitle").HasMaxLength(500).HasColumnType("character varying(500)");
            b.Property<string>("Title").IsRequired().HasMaxLength(160).HasColumnType("character varying(160)");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("IsEnabled", "SortOrder");
            b.HasIndex("Key").IsUnique();
            b.ToTable("home_sections");
        });

        modelBuilder.Entity("Zetruv.Api.Features.Site.SiteFooterLink", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("Group").IsRequired().HasMaxLength(30).HasColumnType("character varying(30)");
            b.Property<bool>("IsActive").HasColumnType("boolean");
            b.Property<string>("Label").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<int>("SortOrder").HasColumnType("integer");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("Url").IsRequired().HasMaxLength(500).HasColumnType("character varying(500)");
            b.HasKey("Id");
            b.HasIndex("Group", "IsActive", "SortOrder");
            b.ToTable("site_footer_links");
        });

        modelBuilder.Entity("Zetruv.Api.Features.Site.SitePaymentMethod", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<string>("Code").IsRequired().HasMaxLength(80).HasColumnType("character varying(80)");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("IconUrl").HasMaxLength(1000).HasColumnType("character varying(1000)");
            b.Property<bool>("IsActive").HasColumnType("boolean");
            b.Property<string>("Name").IsRequired().HasMaxLength(120).HasColumnType("character varying(120)");
            b.Property<int>("SortOrder").HasColumnType("integer");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("Code").IsUnique();
            b.HasIndex("IsActive", "SortOrder");
            b.ToTable("site_payment_methods");
        });

        modelBuilder.Entity("Zetruv.Api.Features.Site.SiteSetting", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<string>("BrandDescription").IsRequired().HasMaxLength(1000).HasColumnType("character varying(1000)");
            b.Property<string>("ContactTeamLabel").IsRequired().HasMaxLength(80).HasColumnType("character varying(80)");
            b.Property<string>("ContactTeamUrl").HasMaxLength(500).HasColumnType("character varying(500)");
            b.Property<string>("CopyrightText").IsRequired().HasMaxLength(250).HasColumnType("character varying(250)");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("LogoUrl").HasMaxLength(1000).HasColumnType("character varying(1000)");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.ToTable("site_settings");
        });

        modelBuilder.Entity("Zetruv.Api.Features.Site.SiteSocialLink", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("IconUrl").HasMaxLength(1000).HasColumnType("character varying(1000)");
            b.Property<bool>("IsActive").HasColumnType("boolean");
            b.Property<string>("Platform").IsRequired().HasMaxLength(80).HasColumnType("character varying(80)");
            b.Property<int>("SortOrder").HasColumnType("integer");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("Url").IsRequired().HasMaxLength(500).HasColumnType("character varying(500)");
            b.HasKey("Id");
            b.HasIndex("IsActive", "SortOrder");
            b.ToTable("site_social_links");
        });

        modelBuilder.Entity("Zetruv.Api.Features.Articles.Article", b =>
        {
            b.HasOne("Zetruv.Api.Features.Articles.ArticleCategory", "Category")
                .WithMany("Articles")
                .HasForeignKey("CategoryId")
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            b.Navigation("Category");
        });

        modelBuilder.Entity("Zetruv.Api.Features.Catalog.Product", b =>
        {
            b.HasOne("Zetruv.Api.Features.Catalog.CatalogCategory", "Category")
                .WithMany("Products")
                .HasForeignKey("CategoryId")
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            b.HasOne("Zetruv.Api.Features.Catalog.Game", "Game")
                .WithMany("Products")
                .HasForeignKey("GameId")
                .OnDelete(DeleteBehavior.Restrict);
            b.Navigation("Category");
            b.Navigation("Game");
        });

        modelBuilder.Entity("Zetruv.Api.Features.Catalog.ProductImage", b =>
        {
            b.HasOne("Zetruv.Api.Features.Catalog.Product", "Product")
                .WithMany("Images")
                .HasForeignKey("ProductId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.Navigation("Product");
        });

        modelBuilder.Entity("Zetruv.Api.Features.Catalog.ProductVariant", b =>
        {
            b.HasOne("Zetruv.Api.Features.Catalog.Product", "Product")
                .WithMany("Variants")
                .HasForeignKey("ProductId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.Navigation("Product");
        });

        modelBuilder.Entity("Zetruv.Api.Features.Catalog.PromotionItem", b =>
        {
            b.HasOne("Zetruv.Api.Features.Catalog.ProductVariant", "ProductVariant")
                .WithMany()
                .HasForeignKey("ProductVariantId")
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            b.HasOne("Zetruv.Api.Features.Catalog.Promotion", "Promotion")
                .WithMany("Items")
                .HasForeignKey("PromotionId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.Navigation("ProductVariant");
            b.Navigation("Promotion");
        });

        modelBuilder.Entity("Zetruv.Api.Features.Articles.ArticleCategory", b =>
        {
            b.Navigation("Articles");
        });
        modelBuilder.Entity("Zetruv.Api.Features.Catalog.CatalogCategory", b =>
        {
            b.Navigation("Products");
        });
        modelBuilder.Entity("Zetruv.Api.Features.Catalog.Game", b =>
        {
            b.Navigation("Products");
        });
        modelBuilder.Entity("Zetruv.Api.Features.Catalog.Product", b =>
        {
            b.Navigation("Images");
            b.Navigation("Variants");
        });
        modelBuilder.Entity("Zetruv.Api.Features.Catalog.Promotion", b =>
        {
            b.Navigation("Items");
        });
#pragma warning restore 612, 618
    }
}
