using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations
{
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
#pragma warning restore 612, 618
        }
    }
}
