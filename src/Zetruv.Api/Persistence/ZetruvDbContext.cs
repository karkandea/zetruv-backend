using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Features.Home;

namespace Zetruv.Api.Persistence
{
    public sealed class ZetruvDbContext(
        DbContextOptions<ZetruvDbContext> options) : DbContext(options)
    {
        public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
        public DbSet<HomeHero> HomeHeroes => Set<HomeHero>();
        public DbSet<HomeSection> HomeSections => Set<HomeSection>();

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
        }
    }
}
