using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Features.Catalog;
using Zetruv.Api.Features.Home;

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
    }
}
