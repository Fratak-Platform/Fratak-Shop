using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <inheritdoc />
    public partial class StorefrontSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BrandId",
                table: "Products",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EnglishName",
                table: "Products",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SalesCount",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ViewCount",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Position",
                table: "Banners",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Brands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    EnglishName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Slug = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    LogoName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Seo_Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Seo_MetaDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Seo_CanonicalUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    Seo_MetaKeywords = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Seo_FocusKeyword = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Seo_BreadcrumbTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Seo_AuthorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Seo_PublisherName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Seo_Language = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Seo_Topic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Seo_NoIndex = table.Column<bool>(type: "bit", nullable: true),
                    Seo_NoFollow = table.Column<bool>(type: "bit", nullable: true),
                    Seo_NoSnippet = table.Column<bool>(type: "bit", nullable: true),
                    Seo_NoImageIndex = table.Column<bool>(type: "bit", nullable: true),
                    Seo_NoTranslate = table.Column<bool>(type: "bit", nullable: true),
                    Seo_IndexIfEmbedded = table.Column<bool>(type: "bit", nullable: true),
                    Seo_MaxSnippet = table.Column<int>(type: "int", nullable: true),
                    Seo_MaxImagePreview = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Seo_MaxVideoPreview = table.Column<int>(type: "int", nullable: true),
                    Seo_OgTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Seo_OgDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Seo_OgUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    Seo_OgType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Seo_OgSiteName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Seo_OgLocale = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Seo_OgLocaleAlternatesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Seo_OgImageName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Seo_OgImageType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Seo_OgImageWidth = table.Column<int>(type: "int", nullable: true),
                    Seo_OgImageHeight = table.Column<int>(type: "int", nullable: true),
                    Seo_OgImageAlt = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Seo_OgVideoUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    Seo_OgAudioUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    Seo_XCard = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Seo_XTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Seo_XDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Seo_XUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    Seo_XImageName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Seo_XImageAlt = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Seo_XSite = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Seo_XCreator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Seo_ArticleSection = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Seo_ArticlePublishedTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Seo_ArticleModifiedTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Seo_ArticleExpirationTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Seo_ArticleAuthorUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    Seo_ArticleTagsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Seo_SchemaType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Seo_StructuredDataJsonLd = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Seo_IncludeInSitemap = table.Column<bool>(type: "bit", nullable: true),
                    Seo_SitemapLastModified = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Seo_SitemapPriority = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Seo_SitemapChangeFrequency = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Seo_SocialImageName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Seo_SocialImageAlt = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Seo_Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Seo_IsReviewed = table.Column<bool>(type: "bit", nullable: true),
                    Seo_ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Seo_ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Brands", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_BrandId",
                table: "Products",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_Brands_Slug",
                table: "Brands",
                column: "Slug",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Brands_BrandId",
                table: "Products",
                column: "BrandId",
                principalTable: "Brands",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_Brands_BrandId",
                table: "Products");

            migrationBuilder.DropTable(
                name: "Brands");

            migrationBuilder.DropIndex(
                name: "IX_Products_BrandId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "BrandId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "EnglishName",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SalesCount",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ViewCount",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Position",
                table: "Banners");
        }
    }
}
