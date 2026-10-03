using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitDataBase : Migration
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
                    Position = table.Column<int>(type: "int", nullable: false),
                    ViewCount = table.Column<int>(type: "int", nullable: false),
                    ClickCount = table.Column<int>(type: "int", nullable: false),
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
                    Seo_SecondaryKeywordsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Seo_HreflangJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Seo_SeoScore = table.Column<int>(type: "int", nullable: true),
                    Seo_LastAuditedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                    Seo_SecondaryKeywordsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Seo_HreflangJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Seo_SeoScore = table.Column<int>(type: "int", nullable: true),
                    Seo_LastAuditedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                name: "ContentPages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Page = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentPages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentVisits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityType = table.Column<int>(type: "int", nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VisitDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentVisits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Coupons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DiscountType = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    MinOrderAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: true),
                    MaxDiscountAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: true),
                    UsageLimit = table.Column<int>(type: "int", nullable: true),
                    UsedCount = table.Column<int>(type: "int", nullable: false),
                    PerUserLimit = table.Column<int>(type: "int", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Coupons", x => x.Id);
                    table.CheckConstraint("CK_Coupons_Value", "[Value] >= 0 AND [UsedCount] >= 0 AND [PerUserLimit] >= 0 AND ([UsageLimit] IS NULL OR [UsageLimit] >= 0)");
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
                name: "MenuItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Location = table.Column<int>(type: "int", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Icon = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BadgeText = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    BadgeColor = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OpenInNewTab = table.Column<bool>(type: "bit", nullable: false),
                    ShowOnMobile = table.Column<bool>(type: "bit", nullable: false),
                    IsHighlighted = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MenuItems_MenuItems_ParentId",
                        column: x => x.ParentId,
                        principalTable: "MenuItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotFoundLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Path = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Referrer = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    HitCount = table.Column<int>(type: "int", nullable: false),
                    LastHitAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResolvedRedirectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsResolved = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotFoundLogs", x => x.Id);
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
                    Seo_SecondaryKeywordsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Seo_HreflangJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Seo_SeoScore = table.Column<int>(type: "int", nullable: true),
                    Seo_LastAuditedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                name: "PaymentCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BankName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CardNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AccountHolder = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ShebaNumber = table.Column<string>(type: "nvarchar(26)", maxLength: 26, nullable: true),
                    BranchName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Instructions = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentCards", x => x.Id);
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
                    HitCount = table.Column<int>(type: "int", nullable: false),
                    LastHitAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                name: "ShippingMethods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Cost = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    FreeShippingThreshold = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: true),
                    EstimatedDeliveryDays = table.Column<int>(type: "int", nullable: true),
                    RestrictedToCity = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Icon = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingMethods", x => x.Id);
                    table.CheckConstraint("CK_ShippingMethods_Cost", "[Cost] >= 0 AND ([FreeShippingThreshold] IS NULL OR [FreeShippingThreshold] >= 0)");
                });

            migrationBuilder.CreateTable(
                name: "SiteSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Group = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ValueType = table.Column<int>(type: "int", nullable: false),
                    Options = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SocialLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Platform = table.Column<int>(type: "int", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ShowInHeader = table.Column<bool>(type: "bit", nullable: false),
                    ShowInFooter = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialLinks", x => x.Id);
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
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    AvatarImageName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
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
                    Seo_SecondaryKeywordsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Seo_HreflangJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Seo_SeoScore = table.Column<int>(type: "int", nullable: true),
                    Seo_LastAuditedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Seo_Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Seo_IsReviewed = table.Column<bool>(type: "bit", nullable: true),
                    Seo_ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Seo_ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsFeatured = table.Column<bool>(type: "bit", nullable: false),
                    ViewCount = table.Column<int>(type: "int", nullable: false),
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
                name: "Addresses",
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
                    table.PrimaryKey("PK_Addresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Addresses_Locations_CityId",
                        column: x => x.CityId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Addresses_Locations_ProvinceId",
                        column: x => x.ProvinceId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Addresses_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLogs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "BlogPosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BlogCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Excerpt = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImageName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    ImageAlt = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    ViewCount = table.Column<int>(type: "int", nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    IsFeatured = table.Column<bool>(type: "bit", nullable: false),
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
                    Seo_SecondaryKeywordsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Seo_HreflangJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Seo_SeoScore = table.Column<int>(type: "int", nullable: true),
                    Seo_LastAuditedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Carts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CouponCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
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
                    Reply = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RepliedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Messages_Users_RepliedByUserId",
                        column: x => x.RepliedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Messages_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsBroadcast = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Notifications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                    TrackingUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    CouponId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ShippingMethodId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AdminNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                        name: "FK_Orders_Coupons_CouponId",
                        column: x => x.CouponId,
                        principalTable: "Coupons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Orders_ShippingMethods_ShippingMethodId",
                        column: x => x.ShippingMethodId,
                        principalTable: "ShippingMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Orders_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SeoAuditRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityType = table.Column<int>(type: "int", nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AppliedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeoAuditRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeoAuditRecords_Users_AppliedByUserId",
                        column: x => x.AppliedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SeoAuditRecords_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "UserRoleAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    GrantedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoleAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserRoleAssignments_Users_GrantedByUserId",
                        column: x => x.GrantedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRoleAssignments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false),
                    CreatedByIp = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserTokens_Users_UserId",
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
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                name: "BankTransferPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentCardId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    PayerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PayerCardNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PayerBankName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TransferDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TrackingNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CustomerNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AdminNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankTransferPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankTransferPayments_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BankTransferPayments_PaymentCards_PaymentCardId",
                        column: x => x.PaymentCardId,
                        principalTable: "PaymentCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BankTransferPayments_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CouponUsages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CouponId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CouponUsages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CouponUsages_Coupons_CouponId",
                        column: x => x.CouponId,
                        principalTable: "Coupons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CouponUsages_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CouponUsages_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
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

            migrationBuilder.CreateTable(
                name: "OrderStatusHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PreviousStatus = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ChangedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsCustomerVisible = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderStatusHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderStatusHistories_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderStatusHistories_Users_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PaymentTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Gateway = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Authority = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ReferenceId = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CardMask = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RequestPayload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VerifyPayload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RedirectUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedByIp = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentTransactions_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BankTransferReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BankTransferPaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    OriginalName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankTransferReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankTransferReceipts_BankTransferPayments_BankTransferPaymentId",
                        column: x => x.BankTransferPaymentId,
                        principalTable: "BankTransferPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Addresses_CityId",
                table: "Addresses",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_Addresses_ProvinceId",
                table: "Addresses",
                column: "ProvinceId");

            migrationBuilder.CreateIndex(
                name: "IX_Addresses_UserId",
                table: "Addresses",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Action",
                table: "AuditLogs",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_EntityId",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransferPayments_OrderId",
                table: "BankTransferPayments",
                column: "OrderId",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransferPayments_PaymentCardId",
                table: "BankTransferPayments",
                column: "PaymentCardId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransferPayments_ReviewedByUserId",
                table: "BankTransferPayments",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransferPayments_Status",
                table: "BankTransferPayments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransferReceipts_BankTransferPaymentId",
                table: "BankTransferReceipts",
                column: "BankTransferPaymentId");

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
                name: "IX_Carts_UpdatedAt",
                table: "Carts",
                column: "UpdatedAt");

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
                name: "IX_ContentPages_Page",
                table: "ContentPages",
                column: "Page",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ContentVisits_EntityType_EntityId_VisitDate",
                table: "ContentVisits",
                columns: new[] { "EntityType", "EntityId", "VisitDate" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ContentVisits_VisitDate",
                table: "ContentVisits",
                column: "VisitDate");

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_Code",
                table: "Coupons",
                column: "Code",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_IsActive",
                table: "Coupons",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_CouponUsages_CouponId",
                table: "CouponUsages",
                column: "CouponId");

            migrationBuilder.CreateIndex(
                name: "IX_CouponUsages_OrderId",
                table: "CouponUsages",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CouponUsages_UserId",
                table: "CouponUsages",
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
                name: "IX_MenuItems_Location",
                table: "MenuItems",
                column: "Location");

            migrationBuilder.CreateIndex(
                name: "IX_MenuItems_Location_DisplayOrder",
                table: "MenuItems",
                columns: new[] { "Location", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_MenuItems_ParentId",
                table: "MenuItems",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_RepliedByUserId",
                table: "Messages",
                column: "RepliedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_Status",
                table: "Messages",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_UserId",
                table: "Messages",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_NotFoundLogs_IsResolved",
                table: "NotFoundLogs",
                column: "IsResolved");

            migrationBuilder.CreateIndex(
                name: "IX_NotFoundLogs_Path",
                table: "NotFoundLogs",
                column: "Path",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_CreatedByUserId",
                table: "Notifications",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Type",
                table: "Notifications",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_IsRead",
                table: "Notifications",
                columns: new[] { "UserId", "IsRead" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_OrderId",
                table: "OrderItems",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_ProductId",
                table: "OrderItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CouponId",
                table: "Orders",
                column: "CouponId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_OrderNumber",
                table: "Orders",
                column: "OrderNumber",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ShippingMethodId",
                table: "Orders",
                column: "ShippingMethodId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Status",
                table: "Orders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_UserId",
                table: "Orders",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderStatusHistories_ChangedByUserId",
                table: "OrderStatusHistories",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderStatusHistories_OrderId",
                table: "OrderStatusHistories",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderStatusHistories_Status",
                table: "OrderStatusHistories",
                column: "Status");

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
                name: "IX_PaymentCards_CardNumber",
                table: "PaymentCards",
                column: "CardNumber",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCards_IsActive",
                table: "PaymentCards",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_Authority",
                table: "PaymentTransactions",
                column: "Authority");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_OrderId",
                table: "PaymentTransactions",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_Status",
                table: "PaymentTransactions",
                column: "Status");

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
                name: "IX_SeoAuditRecords_AppliedByUserId",
                table: "SeoAuditRecords",
                column: "AppliedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SeoAuditRecords_CreatedByUserId",
                table: "SeoAuditRecords",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SeoAuditRecords_EntityType_EntityId",
                table: "SeoAuditRecords",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_SeoAuditRecords_Status",
                table: "SeoAuditRecords",
                column: "Status");

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
                name: "IX_ShippingMethods_IsActive",
                table: "ShippingMethods",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_SiteSettings_Group",
                table: "SiteSettings",
                column: "Group");

            migrationBuilder.CreateIndex(
                name: "IX_SiteSettings_Key",
                table: "SiteSettings",
                column: "Key",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SocialLinks_IsActive",
                table: "SocialLinks",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_SocialLinks_Platform",
                table: "SocialLinks",
                column: "Platform");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleAssignments_GrantedByUserId",
                table: "UserRoleAssignments",
                column: "GrantedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleAssignments_Role",
                table: "UserRoleAssignments",
                column: "Role");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleAssignments_UserId_Role",
                table: "UserRoleAssignments",
                columns: new[] { "UserId", "Role" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Users_IsActive",
                table: "Users",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Users_PhoneNumber",
                table: "Users",
                column: "PhoneNumber",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Role",
                table: "Users",
                column: "Role");

            migrationBuilder.CreateIndex(
                name: "IX_UserTokens_ExpiresAt",
                table: "UserTokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_UserTokens_UserId_Purpose",
                table: "UserTokens",
                columns: new[] { "UserId", "Purpose" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Addresses");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "BankTransferReceipts");

            migrationBuilder.DropTable(
                name: "Banners");

            migrationBuilder.DropTable(
                name: "CartItems");

            migrationBuilder.DropTable(
                name: "Comments");

            migrationBuilder.DropTable(
                name: "ContentPages");

            migrationBuilder.DropTable(
                name: "ContentVisits");

            migrationBuilder.DropTable(
                name: "CouponUsages");

            migrationBuilder.DropTable(
                name: "FaqItems");

            migrationBuilder.DropTable(
                name: "Favorites");

            migrationBuilder.DropTable(
                name: "MenuItems");

            migrationBuilder.DropTable(
                name: "Messages");

            migrationBuilder.DropTable(
                name: "NotFoundLogs");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "OrderItems");

            migrationBuilder.DropTable(
                name: "OrderStatusHistories");

            migrationBuilder.DropTable(
                name: "PageAlerts");

            migrationBuilder.DropTable(
                name: "PageSeos");

            migrationBuilder.DropTable(
                name: "PaymentTransactions");

            migrationBuilder.DropTable(
                name: "ProductFeatures");

            migrationBuilder.DropTable(
                name: "ProductImages");

            migrationBuilder.DropTable(
                name: "SeoAuditRecords");

            migrationBuilder.DropTable(
                name: "SeoRedirects");

            migrationBuilder.DropTable(
                name: "SiteSettings");

            migrationBuilder.DropTable(
                name: "SocialLinks");

            migrationBuilder.DropTable(
                name: "UserRoleAssignments");

            migrationBuilder.DropTable(
                name: "UserTokens");

            migrationBuilder.DropTable(
                name: "Locations");

            migrationBuilder.DropTable(
                name: "BankTransferPayments");

            migrationBuilder.DropTable(
                name: "Carts");

            migrationBuilder.DropTable(
                name: "BlogPosts");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "PaymentCards");

            migrationBuilder.DropTable(
                name: "BlogCategories");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "Coupons");

            migrationBuilder.DropTable(
                name: "ShippingMethods");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
