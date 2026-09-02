using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Zetruv.Api.Persistence;

#nullable disable

namespace Zetruv.Api.Persistence.Migrations;

[DbContext(typeof(ZetruvDbContext))]
[Migration("20260831073500_AddArticlesAndSiteSettings")]
public partial class AddArticlesAndSiteSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "article_categories",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_article_categories", x => x.Id));

        migrationBuilder.CreateTable(
            name: "site_settings",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                LogoUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                BrandDescription = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                CopyrightText = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                ContactTeamLabel = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                ContactTeamUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_site_settings", x => x.Id));

        migrationBuilder.CreateTable(
            name: "site_footer_links",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Group = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_site_footer_links", x => x.Id));

        migrationBuilder.CreateTable(
            name: "site_social_links",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Platform = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                IconUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_site_social_links", x => x.Id));

        migrationBuilder.CreateTable(
            name: "site_payment_methods",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                IconUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_site_payment_methods", x => x.Id));

        migrationBuilder.CreateTable(
            name: "articles",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                Title = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                Slug = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                Excerpt = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                Content = table.Column<string>(type: "text", nullable: false),
                ThumbnailUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                AuthorName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                IsFeatured = table.Column<bool>(type: "boolean", nullable: false),
                PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_articles", x => x.Id);
                table.ForeignKey(
                    name: "FK_articles_article_categories_CategoryId",
                    column: x => x.CategoryId,
                    principalTable: "article_categories",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_article_categories_IsActive_SortOrder",
            table: "article_categories",
            columns: new[] { "IsActive", "SortOrder" });
        migrationBuilder.CreateIndex(
            name: "IX_article_categories_Slug",
            table: "article_categories",
            column: "Slug",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_articles_CategoryId",
            table: "articles",
            column: "CategoryId");
        migrationBuilder.CreateIndex(
            name: "IX_articles_IsPublished_PublishedAt",
            table: "articles",
            columns: new[] { "IsPublished", "PublishedAt" });
        migrationBuilder.CreateIndex(
            name: "IX_articles_Slug",
            table: "articles",
            column: "Slug",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_site_footer_links_Group_IsActive_SortOrder",
            table: "site_footer_links",
            columns: new[] { "Group", "IsActive", "SortOrder" });
        migrationBuilder.CreateIndex(
            name: "IX_site_payment_methods_Code",
            table: "site_payment_methods",
            column: "Code",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_site_payment_methods_IsActive_SortOrder",
            table: "site_payment_methods",
            columns: new[] { "IsActive", "SortOrder" });
        migrationBuilder.CreateIndex(
            name: "IX_site_social_links_IsActive_SortOrder",
            table: "site_social_links",
            columns: new[] { "IsActive", "SortOrder" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "articles");
        migrationBuilder.DropTable(name: "site_footer_links");
        migrationBuilder.DropTable(name: "site_payment_methods");
        migrationBuilder.DropTable(name: "site_settings");
        migrationBuilder.DropTable(name: "site_social_links");
        migrationBuilder.DropTable(name: "article_categories");
    }
}
