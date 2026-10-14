using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <inheritdoc />
    public partial class initdatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Banners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Subtitle = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ImageName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    MobileImageName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    ImageAlt = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    ButtonText = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LinkUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    StartAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Banners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BlogCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
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
                    table.PrimaryKey("PK_BlogCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ImageName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
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
                    table.PrimaryKey("PK_Categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Categories_Categories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FaqItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Question = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Answer = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaqItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Locations_Locations_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PageAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Page = table.Column<int>(type: "int", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    StartAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageAlerts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PageSeos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Page = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_PageSeos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeoRedirects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ToUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeoRedirects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Role = table.Column<int>(type: "int", nullable: false),
                    IsPhoneVerified = table.Column<bool>(type: "bit", nullable: false),
                    IsEmailVerified = table.Column<bool>(type: "bit", nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    SKU = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ShortDescription = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Price = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    DiscountPrice = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: true),
                    DiscountStartAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DiscountEndAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StockQuantity = table.Column<int>(type: "int", nullable: false),
                    WarrantyTitle = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    WarrantyMonths = table.Column<int>(type: "int", nullable: true),
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
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.CheckConstraint("CK_Products_PriceAndStock", "[Price] >= 0 AND ([DiscountPrice] IS NULL OR [DiscountPrice] >= 0) AND [StockQuantity] >= 0 AND ([WarrantyMonths] IS NULL OR [WarrantyMonths] >= 0)");
                    table.ForeignKey(
                        name: "FK_Products_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BlogPosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BlogCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Excerpt = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImageName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    ImageAlt = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    ViewCount = table.Column<int>(type: "int", nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                    table.PrimaryKey("PK_BlogPosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlogPosts_BlogCategories_BlogCategoryId",
                        column: x => x.BlogCategoryId,
                        principalTable: "BlogCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BlogPosts_Users_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Carts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Carts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Carts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RepliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Messages_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OrderNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PaymentMethod = table.Column<int>(type: "int", nullable: false),
                    PaymentStatus = table.Column<int>(type: "int", nullable: false),
                    TransactionId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TrackingCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SubtotalAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    ShippingAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    PayableAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    ShippingMethod = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RequestedDeliveryDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CustomerName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    CustomerPhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ShippingProvince = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ShippingCity = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ShippingAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ShippingBuildingNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ShippingUnit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ShippingPostalCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ShippingPhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CustomerNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                    table.CheckConstraint("CK_Orders_Amounts", "[SubtotalAmount] >= 0 AND [DiscountAmount] >= 0 AND [ShippingAmount] >= 0 AND [PayableAmount] >= 0");
                    table.ForeignKey(
                        name: "FK_Orders_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "UserAddresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProvinceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AddressLine = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    BuildingNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PostalCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAddresses_Locations_CityId",
                        column: x => x.CityId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserAddresses_Locations_ProvinceId",
                        column: x => x.ProvinceId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserAddresses_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Favorites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Favorites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Favorites_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Favorites_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductFeatures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ValueType = table.Column<int>(type: "int", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductFeatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductFeatures_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImageName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    AltText = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductImages_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Comments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BlogPostId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    AuthorName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    AuthorPhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Rating = table.Column<byte>(type: "tinyint", nullable: true),
                    IsRecommended = table.Column<bool>(type: "bit", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comments", x => x.Id);
                    table.CheckConstraint("CK_Comments_ProductOrBlogPost", "([ProductId] IS NOT NULL AND [BlogPostId] IS NULL) OR ([ProductId] IS NULL AND [BlogPostId] IS NOT NULL)");
                    table.CheckConstraint("CK_Comments_Rating", "[Rating] IS NULL OR ([Rating] >= 1 AND [Rating] <= 5)");
                    table.ForeignKey(
                        name: "FK_Comments_BlogPosts_BlogPostId",
                        column: x => x.BlogPostId,
                        principalTable: "BlogPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Comments_Comments_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Comments_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Comments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CartItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CartId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CartItems", x => x.Id);
                    table.CheckConstraint("CK_CartItems_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_CartItems_Carts_CartId",
                        column: x => x.CartId,
                        principalTable: "Carts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CartItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProductName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    SKU = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItems", x => x.Id);
                    table.CheckConstraint("CK_OrderItems_Amounts", "[Quantity] > 0 AND [UnitPrice] >= 0 AND [DiscountAmount] >= 0 AND [TotalAmount] >= 0");
                    table.ForeignKey(
                        name: "FK_OrderItems_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlogCategories_Slug",
                table: "BlogCategories",
                column: "Slug",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_BlogPosts_AuthorId",
                table: "BlogPosts",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_BlogPosts_BlogCategoryId",
                table: "BlogPosts",
                column: "BlogCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_BlogPosts_IsPublished",
                table: "BlogPosts",
                column: "IsPublished");

            migrationBuilder.CreateIndex(
                name: "IX_BlogPosts_Slug",
                table: "BlogPosts",
                column: "Slug",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_CartId_ProductId",
                table: "CartItems",
                columns: new[] { "CartId", "ProductId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_ProductId",
                table: "CartItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Carts_SessionKey",
                table: "Carts",
                column: "SessionKey");

            migrationBuilder.CreateIndex(
                name: "IX_Carts_UserId",
                table: "Carts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_ParentId",
                table: "Categories",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Slug",
                table: "Categories",
                column: "Slug",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_BlogPostId",
                table: "Comments",
                column: "BlogPostId");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_ParentId",
                table: "Comments",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_ProductId",
                table: "Comments",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_Status",
                table: "Comments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_UserId",
                table: "Comments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Favorites_ProductId",
                table: "Favorites",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Favorites_UserId_ProductId",
                table: "Favorites",
                columns: new[] { "UserId", "ProductId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_Code",
                table: "Locations",
                column: "Code",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_ParentId",
                table: "Locations",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_Type",
                table: "Locations",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_Status",
                table: "Messages",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_UserId",
                table: "Messages",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_OrderId",
                table: "OrderItems",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_ProductId",
                table: "OrderItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_OrderNumber",
                table: "Orders",
                column: "OrderNumber",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Status",
                table: "Orders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_UserId",
                table: "Orders",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PageAlerts_IsActive",
                table: "PageAlerts",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_PageAlerts_Page_Position",
                table: "PageAlerts",
                columns: new[] { "Page", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_PageSeos_Page",
                table: "PageSeos",
                column: "Page",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ProductFeatures_ProductId_Key",
                table: "ProductFeatures",
                columns: new[] { "ProductId", "Key" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_ProductId",
                table: "ProductImages",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_CategoryId",
                table: "Products",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_SKU",
                table: "Products",
                column: "SKU",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Slug",
                table: "Products",
                column: "Slug",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SeoRedirects_FromPath",
                table: "SeoRedirects",
                column: "FromPath",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SeoRedirects_IsActive",
                table: "SeoRedirects",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_UserAddresses_CityId",
                table: "UserAddresses",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAddresses_ProvinceId",
                table: "UserAddresses",
                column: "ProvinceId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAddresses_UserId",
                table: "UserAddresses",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_PhoneNumber",
                table: "Users",
                column: "PhoneNumber",
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Banners");

            migrationBuilder.DropTable(
                name: "CartItems");

            migrationBuilder.DropTable(
                name: "Comments");

            migrationBuilder.DropTable(
                name: "FaqItems");

            migrationBuilder.DropTable(
                name: "Favorites");

            migrationBuilder.DropTable(
                name: "Messages");

            migrationBuilder.DropTable(
                name: "OrderItems");

            migrationBuilder.DropTable(
                name: "PageAlerts");

            migrationBuilder.DropTable(
                name: "PageSeos");

            migrationBuilder.DropTable(
                name: "ProductFeatures");

            migrationBuilder.DropTable(
                name: "ProductImages");

            migrationBuilder.DropTable(
                name: "SeoRedirects");

            migrationBuilder.DropTable(
                name: "UserAddresses");

            migrationBuilder.DropTable(
                name: "Carts");

            migrationBuilder.DropTable(
                name: "BlogPosts");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "Locations");

            migrationBuilder.DropTable(
                name: "BlogCategories");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Categories");
        }
    }
}
