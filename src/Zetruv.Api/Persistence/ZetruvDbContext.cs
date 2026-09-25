using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Articles;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Features.Catalog;
using Zetruv.Api.Features.Home;
using Zetruv.Api.Features.Media;
using Zetruv.Api.Features.Orders;
using Zetruv.Api.Features.Payments;
using Zetruv.Api.Features.Site;

namespace Zetruv.Api.Persistence;

public sealed class ZetruvDbContext(
    DbContextOptions<ZetruvDbContext> options) : DbContext(options)
{
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<CustomerUser> CustomerUsers => Set<CustomerUser>();
    public DbSet<CustomerAuthToken> CustomerAuthTokens => Set<CustomerAuthToken>();
    public DbSet<CustomerPasswordHistory> CustomerPasswordHistories => Set<CustomerPasswordHistory>();
    public DbSet<HomeHero> HomeHeroes => Set<HomeHero>();
    public DbSet<HomeSection> HomeSections => Set<HomeSection>();
    public DbSet<CatalogCategory> CatalogCategories => Set<CatalogCategory>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<GameAccountDetails> GameAccountDetails => Set<GameAccountDetails>();
    public DbSet<GameAccountAttributeDefinition> GameAccountAttributeDefinitions =>
        Set<GameAccountAttributeDefinition>();
    public DbSet<ProductReview> ProductReviews => Set<ProductReview>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductInputField> ProductInputFields => Set<ProductInputField>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<PromotionItem> PromotionItems => Set<PromotionItem>();
    public DbSet<ArticleCategory> ArticleCategories => Set<ArticleCategory>();
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<DiscountVoucher> DiscountVouchers => Set<DiscountVoucher>();
    public DbSet<DiscountVoucherRedemption> DiscountVoucherRedemptions => Set<DiscountVoucherRedemption>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<CustomerCartItem> CustomerCartItems => Set<CustomerCartItem>();
    public DbSet<FulfillmentActivity> FulfillmentActivities => Set<FulfillmentActivity>();
    public DbSet<ManualLoginCredential> ManualLoginCredentials => Set<ManualLoginCredential>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<PaymentWebhookEvent> PaymentWebhookEvents => Set<PaymentWebhookEvent>();
    public DbSet<InventoryReservation> InventoryReservations => Set<InventoryReservation>();
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

        modelBuilder.Entity<CustomerUser>(entity =>
        {
            entity.ToTable("customer_users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.Property(x => x.NormalizedEmail).HasMaxLength(320).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(1000).IsRequired();
            entity.HasIndex(x => x.NormalizedEmail).IsUnique();
        });

        modelBuilder.Entity<CustomerAuthToken>(entity =>
        {
            entity.ToTable("customer_auth_tokens");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.CustomerUserId, x.Purpose, x.ExpiresAt });
            entity.HasIndex(x => new { x.CustomerUserId, x.Purpose })
                .IsUnique().HasFilter("\"ConsumedAt\" IS NULL");
            entity.HasOne(x => x.CustomerUser).WithMany()
                .HasForeignKey(x => x.CustomerUserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomerPasswordHistory>(entity =>
        {
            entity.ToTable("customer_password_history");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PasswordHash).HasMaxLength(1000).IsRequired();
            entity.HasIndex(x => new { x.CustomerUserId, x.CreatedAt });
            entity.HasOne(x => x.CustomerUser).WithMany()
                .HasForeignKey(x => x.CustomerUserId).OnDelete(DeleteBehavior.Cascade);
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
            entity.Property(x => x.FulfillmentMethod).HasConversion<string>().HasMaxLength(30).IsRequired();
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

        modelBuilder.Entity<GameAccountAttributeDefinition>(entity =>
        {
            entity.ToTable("game_account_attribute_definitions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Label).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.OptionsJson).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(x => new { x.GameId, x.Key }).IsUnique();
            entity.HasIndex(x => new { x.GameId, x.IsActive, x.SortOrder });
            entity.HasOne(x => x.Game).WithMany()
                .HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GameAccountDetails>(entity =>
        {
            entity.ToTable("game_account_details");
            entity.HasKey(x => x.ProductId);
            entity.Property(x => x.Rank).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Region).HasMaxLength(120).IsRequired();
            entity.Property(x => x.AdditionalInfo).HasMaxLength(1000);
            entity.Property(x => x.AttributesJson).HasColumnType("jsonb").IsRequired()
                .HasDefaultValueSql("'{}'::jsonb");
            entity.HasOne(x => x.Product).WithOne()
                .HasForeignKey<GameAccountDetails>(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductReview>(entity =>
        {
            entity.ToTable("product_reviews");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Comment).HasMaxLength(1000);
            entity.HasIndex(x => x.OrderItemId).IsUnique();
            entity.HasIndex(x => new { x.ProductId, x.IsApproved });
            entity.HasOne<OrderItem>().WithMany().HasForeignKey(x => x.OrderItemId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<CustomerUser>().WithMany().HasForeignKey(x => x.CustomerUserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductVariant>(entity =>
        {
            entity.ToTable("product_variants");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(180).IsRequired();
            entity.Property(x => x.Sku).HasMaxLength(100).IsRequired();
            entity.Property(x => x.GroupName).HasMaxLength(80);
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

        modelBuilder.Entity<ProductInputField>(entity =>
        {
            entity.ToTable("product_input_fields");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasMaxLength(60).IsRequired();
            entity.Property(x => x.Label).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Scope).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.Placeholder).HasMaxLength(160);
            entity.Property(x => x.HelpText).HasMaxLength(500);
            entity.Property(x => x.OptionsJson).HasColumnType("jsonb");
            entity.HasIndex(x => new { x.ProductId, x.Key }).IsUnique();
            entity.HasIndex(x => new { x.ProductId, x.Scope, x.SortOrder });
            entity.HasOne(x => x.Product)
                .WithMany(x => x.InputFields)
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

        modelBuilder.Entity<MediaAsset>(entity =>
        {
            entity.ToTable("media_assets");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Provider).HasMaxLength(80).IsRequired();
            entity.Property(x => x.StorageKey).HasMaxLength(500).IsRequired();
            entity.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => x.StorageKey).IsUnique();
            entity.HasIndex(x => new { x.DeletedAt, x.CreatedAt });
        });

        modelBuilder.Entity<CustomerCartItem>(entity =>
        {
            entity.ToTable("customer_cart_items");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CustomerUserId, x.ProductVariantId }).IsUnique();
            entity.HasOne<CustomerUser>().WithMany().HasForeignKey(x => x.CustomerUserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.ProductVariant).WithMany().HasForeignKey(x => x.ProductVariantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DiscountVoucher>(entity =>
        {
            entity.ToTable("discount_vouchers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Code).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.Value).HasPrecision(18, 2);
            entity.Property(x => x.MaximumDiscount).HasPrecision(18, 2);
            entity.Property(x => x.MinimumSpend).HasPrecision(18, 2);
            entity.Property(x => x.ApplicableKind).HasConversion<string>().HasMaxLength(30);
            entity.HasIndex(x => x.Code).IsUnique();
            entity.HasIndex(x => new { x.IsActive, x.StartsAt, x.EndsAt });
        });
        modelBuilder.Entity<DiscountVoucherRedemption>(entity =>
        {
            entity.ToTable("discount_voucher_redemptions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CustomerKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.HasIndex(x => x.OrderId).IsUnique();
            entity.HasIndex(x => new { x.VoucherId, x.CustomerKey, x.ReleasedAt });
            entity.HasOne(x => x.Voucher).WithMany().HasForeignKey(x => x.VoucherId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasIndex(x => new { x.CustomerUserId, x.PaidAt });
            entity.HasOne<CustomerUser>().WithMany()
                .HasForeignKey(x => x.CustomerUserId).OnDelete(DeleteBehavior.SetNull);
            entity.ToTable("orders");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.OrderNumber).HasMaxLength(40).IsRequired();
            entity.Property(x => x.VoucherCode).HasMaxLength(32);
            entity.Property(x => x.VoucherDiscountAmount).HasPrecision(18, 2);
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
            entity.Property(x => x.FulfillmentMethod).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.FulfillmentStatus).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.FulfillmentReference).HasMaxLength(180);
            entity.Property(x => x.FulfillmentMessage).HasMaxLength(500);
            entity.Property(x => x.VariantName).HasMaxLength(180);
            entity.Property(x => x.Sku).HasMaxLength(100);
            entity.Property(x => x.ThumbnailUrl).HasMaxLength(1000);
            entity.Property(x => x.GameName).HasMaxLength(120);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
            entity.Property(x => x.LineTotal).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.OrderId, x.CreatedAt });
            entity.HasIndex(x => new { x.OrderId, x.FulfillmentStatus });
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

        modelBuilder.Entity<FulfillmentActivity>(entity =>
        {
            entity.ToTable("fulfillment_activities");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(40).IsRequired();
            entity.Property(x => x.Source).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.ActorId).HasMaxLength(100);
            entity.Property(x => x.ActorEmail).HasMaxLength(320);
            entity.Property(x => x.FulfillmentMethod).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.Provider).HasMaxLength(80);
            entity.Property(x => x.ProviderReference).HasMaxLength(180);
            entity.Property(x => x.Message).HasMaxLength(500);
            entity.HasIndex(x => new { x.OrderItemId, x.CreatedAt });
            entity.HasIndex(x => new { x.OrderId, x.CreatedAt });
            entity.HasOne<OrderItem>()
                .WithMany()
                .HasForeignKey(x => x.OrderItemId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Order>()
                .WithMany()
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ManualLoginCredential>(entity =>
        {
            entity.ToTable("manual_login_credentials");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EncryptedPayload).HasColumnType("text");
            entity.Property(x => x.FieldNamesJson).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(x => x.OrderItemId).IsUnique();
            entity.HasOne(x => x.OrderItem)
                .WithOne(x => x.ManualLoginCredential)
                .HasForeignKey<ManualLoginCredential>(x => x.OrderItemId)
                .OnDelete(DeleteBehavior.Cascade);
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
            entity.Property(x => x.PaymentUrl).HasMaxLength(2000);
            entity.Property(x => x.QrString).HasColumnType("text");
            entity.Property(x => x.ReconciliationMessage).HasMaxLength(500);
            entity.HasIndex(x => new { x.OrderId, x.CreatedAt });
            entity.HasIndex(x => new { x.Provider, x.ProviderReference });
            entity.HasIndex(x => new { x.Status, x.NextReconciliationAt });
            entity.HasOne(x => x.Order)
                .WithMany(x => x.Transactions)
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PaymentWebhookEvent>(entity =>
        {
            entity.ToTable("payment_webhook_events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Provider).HasMaxLength(80).IsRequired();
            entity.Property(x => x.ProviderEventId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ProviderReference).HasMaxLength(180).IsRequired();
            entity.Property(x => x.EventFingerprintSha256).HasMaxLength(64).IsRequired();
            entity.Property(x => x.WebhookStatus).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.ResultMessage).HasMaxLength(500);
            entity.HasIndex(x => new { x.Provider, x.ProviderEventId }).IsUnique();
            entity.HasIndex(x => new { x.OrderId, x.ReceivedAt });
        });

        modelBuilder.Entity<InventoryReservation>(entity =>
        {
            entity.ToTable("inventory_reservations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.HasIndex(x => new { x.OrderId, x.ProductVariantId }).IsUnique();
            entity.HasIndex(x => new { x.Status, x.ExpiresAt });
            entity.HasOne(x => x.Order)
                .WithMany()
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ProductVariant>()
                .WithMany()
                .HasForeignKey(x => x.ProductVariantId)
                .OnDelete(DeleteBehavior.Restrict);
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
