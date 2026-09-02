using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Articles;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Features.Catalog;
using Zetruv.Api.Features.Home;
using Zetruv.Api.Features.Orders;
using Zetruv.Api.Features.Site;

namespace Zetruv.Api.Persistence;

public sealed class ZetruvDbContext(
    DbContextOptions<ZetruvDbContext> options) : DbContext(options)
{
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<HomeHero> HomeHeroes => Set<HomeHero>();
    public DbSet<HomeSection> HomeSections => Set<HomeSection>();
    public DbSet<CatalogCategory> CatalogCategories => Set<CatalogCategory>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<PromotionItem> PromotionItems => Set<PromotionItem>();
    public DbSet<ArticleCategory> ArticleCategories => Set<ArticleCategory>();
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<SiteSetting> SiteSettings => Set<SiteSetting>();
    public DbSet<SiteFooterLink> SiteFooterLinks => Set<SiteFooterLink>();
    public DbSet<SiteSocialLink> SiteSocialLinks => Set<SiteSocialLink>();
    public DbSet<SitePaymentMethod> SitePaymentMethods => Set<SitePaymentMethod>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdminUser>(entity =>
        {
            entity.ToTable("admin_users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.Property(x => x.NormalizedEmail).HasMaxLength(320).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Role).HasMaxLength(50).IsRequired();
            entity.HasIndex(x => x.NormalizedEmail).IsUnique();
        });

        modelBuilder.Entity<HomeHero>(entity =>
        {
            entity.ToTable("home_heroes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Subtitle).HasMaxLength(500).IsRequired();
            entity.Property(x => x.ImageUrl).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.PrimaryCtaLabel).HasMaxLength(80);
            entity.Property(x => x.PrimaryCtaUrl).HasMaxLength(500);
            entity.Property(x => x.SecondaryCtaLabel).HasMaxLength(80);
            entity.Property(x => x.SecondaryCtaUrl).HasMaxLength(500);
            entity.HasIndex(x => new { x.IsActive, x.SortOrder });
        });

        modelBuilder.Entity<HomeSection>(entity =>
        {
            entity.ToTable("home_sections");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Subtitle).HasMaxLength(500);
            entity.Property(x => x.CtaLabel).HasMaxLength(80);
            entity.Property(x => x.CtaUrl).HasMaxLength(500);
            entity.HasIndex(x => x.Key).IsUnique();
            entity.HasIndex(x => new { x.IsEnabled, x.SortOrder });
        });

        modelBuilder.Entity<CatalogCategory>(entity =>
        {
            entity.ToTable("catalog_categories");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.IconUrl).HasMaxLength(1000);
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.HasIndex(x => x.Key).IsUnique();
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.HasIndex(x => new { x.IsActive, x.SortOrder });
        });

        modelBuilder.Entity<Game>(entity =>
        {
            entity.ToTable("games");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Publisher).HasMaxLength(120);
            entity.Property(x => x.ImageUrl).HasMaxLength(1000);
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.HasIndex(x => new { x.IsActive, x.IsPopular, x.SortOrder });
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("products");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(180).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(220).IsRequired();
            entity.Property(x => x.ShortDescription).HasMaxLength(500);
            entity.Property(x => x.ThumbnailUrl).HasMaxLength(1000);
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.HasIndex(x => new { x.IsActive, x.Kind, x.SortOrder });
            entity.HasIndex(x => x.GameId);
            entity.HasOne(x => x.Category)
                .WithMany(x => x.Products)
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Game)
                .WithMany(x => x.Products)
                .HasForeignKey(x => x.GameId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProductVariant>(entity =>
        {
            entity.ToTable("product_variants");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(180).IsRequired();
            entity.Property(x => x.Sku).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Price).HasPrecision(18, 2);
            entity.Property(x => x.CompareAtPrice).HasPrecision(18, 2);
            entity.HasIndex(x => x.Sku).IsUnique();
            entity.HasIndex(x => new { x.ProductId, x.IsActive, x.SortOrder });
            entity.HasOne(x => x.Product)
                .WithMany(x => x.Variants)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductImage>(entity =>
        {
            entity.ToTable("product_images");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Url).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.AltText).HasMaxLength(250);
            entity.HasIndex(x => new { x.ProductId, x.SortOrder });
            entity.HasOne(x => x.Product)
                .WithMany(x => x.Images)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Promotion>(entity =>
        {
            entity.ToTable("promotions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(180).IsRequired();
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.HasIndex(x => new { x.IsActive, x.IsFlashSale, x.StartsAt, x.EndsAt });
        });

        modelBuilder.Entity<PromotionItem>(entity =>
        {
            entity.ToTable("promotion_items");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SalePrice).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.PromotionId, x.SortOrder });
            entity.HasIndex(x => new { x.PromotionId, x.ProductVariantId }).IsUnique();
            entity.HasOne(x => x.Promotion)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.PromotionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.ProductVariant)
                .WithMany()
                .HasForeignKey(x => x.ProductVariantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ArticleCategory>(entity =>
        {
            entity.ToTable("article_categories");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(160).IsRequired();
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.HasIndex(x => new { x.IsActive, x.SortOrder });
        });

        modelBuilder.Entity<Article>(entity =>
        {
            entity.ToTable("articles");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(220).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(240).IsRequired();
            entity.Property(x => x.Excerpt).HasMaxLength(600).IsRequired();
            entity.Property(x => x.Content).HasColumnType("text").IsRequired();
            entity.Property(x => x.ThumbnailUrl).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.AuthorName).HasMaxLength(120);
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.HasIndex(x => new { x.IsPublished, x.PublishedAt });
            entity.HasIndex(x => x.CategoryId);
            entity.HasOne(x => x.Category)
                .WithMany(x => x.Articles)
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("orders");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.OrderNumber).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.PaymentStatus).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.CustomerName).HasMaxLength(160);
            entity.Property(x => x.CustomerEmail).HasMaxLength(320);
            entity.Property(x => x.CustomerPhone).HasMaxLength(50);
            entity.Property(x => x.Subtotal).HasPrecision(18, 2);
            entity.Property(x => x.DiscountAmount).HasPrecision(18, 2);
            entity.Property(x => x.ShippingAmount).HasPrecision(18, 2);
            entity.Property(x => x.GrandTotal).HasPrecision(18, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.PaymentProvider).HasMaxLength(80);
            entity.Property(x => x.PaymentReference).HasMaxLength(180);
            entity.HasIndex(x => x.OrderNumber).IsUnique();
            entity.HasIndex(x => new { x.Status, x.PaymentStatus, x.CreatedAt });
            entity.HasIndex(x => x.PaymentReference);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("order_items");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProductName).HasMaxLength(180).IsRequired();
            entity.Property(x => x.ProductSlug).HasMaxLength(220).IsRequired();
            entity.Property(x => x.ProductKind).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.VariantName).HasMaxLength(180);
            entity.Property(x => x.Sku).HasMaxLength(100);
            entity.Property(x => x.ThumbnailUrl).HasMaxLength(1000);
            entity.Property(x => x.GameName).HasMaxLength(120);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
            entity.Property(x => x.LineTotal).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.OrderId, x.CreatedAt });
            entity.HasIndex(x => x.ProductId);
            entity.HasIndex(x => x.ProductVariantId);
            entity.HasOne(x => x.Order)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Product)
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.ProductVariant)
                .WithMany()
                .HasForeignKey(x => x.ProductVariantId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PaymentTransaction>(entity =>
        {
            entity.ToTable("payment_transactions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Provider).HasMaxLength(80).IsRequired();
            entity.Property(x => x.ProviderReference).HasMaxLength(180);
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.HasIndex(x => new { x.OrderId, x.CreatedAt });
            entity.HasIndex(x => new { x.Provider, x.ProviderReference });
            entity.HasOne(x => x.Order)
                .WithMany(x => x.Transactions)
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SiteSetting>(entity =>
        {
            entity.ToTable("site_settings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.LogoUrl).HasMaxLength(1000);
            entity.Property(x => x.BrandDescription).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.CopyrightText).HasMaxLength(250).IsRequired();
            entity.Property(x => x.ContactTeamLabel).HasMaxLength(80).IsRequired();
            entity.Property(x => x.ContactTeamUrl).HasMaxLength(500);
        });

        modelBuilder.Entity<SiteFooterLink>(entity =>
        {
            entity.ToTable("site_footer_links");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Group).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.Label).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Url).HasMaxLength(500).IsRequired();
            entity.HasIndex(x => new { x.Group, x.IsActive, x.SortOrder });
        });

        modelBuilder.Entity<SiteSocialLink>(entity =>
        {
            entity.ToTable("site_social_links");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Platform).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Url).HasMaxLength(500).IsRequired();
            entity.Property(x => x.IconUrl).HasMaxLength(1000);
            entity.HasIndex(x => new { x.IsActive, x.SortOrder });
        });

        modelBuilder.Entity<SitePaymentMethod>(entity =>
        {
            entity.ToTable("site_payment_methods");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Code).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.IconUrl).HasMaxLength(1000);
            entity.HasIndex(x => x.Code).IsUnique();
            entity.HasIndex(x => new { x.IsActive, x.SortOrder });
        });
    }
}
