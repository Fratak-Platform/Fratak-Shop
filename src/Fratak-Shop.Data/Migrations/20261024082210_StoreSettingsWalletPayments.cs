using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <inheritdoc />
    public partial class StoreSettingsWalletPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Amounts",
                table: "Orders");

            migrationBuilder.AddColumn<int>(
                name: "FailedLoginCount",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockoutEndAt",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                table: "Users",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WalletBalance",
                table: "Users",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "Orders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CancelledBy",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeliveryTimeSlotId",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryTimeSlotTitle",
                table: "Orders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PaymentDeadline",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RefundedAmount",
                table: "Orders",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RefundedAt",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ShippingMethodId",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "FaqItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContentPages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PageKey = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ShowInFooter = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_ContentPages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FaqCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IconKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaqCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HomeSections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Subtitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    LinkUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HomeSections", x => x.Id);
                    table.CheckConstraint("CK_HomeSections_ItemCount", "[ItemCount] >= 0 AND [ItemCount] <= 50");
                    table.ForeignKey(
                        name: "FK_HomeSections_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PaymentTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    Gateway = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Authority = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RefId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CardPan = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ResultCode = table.Column<int>(type: "int", nullable: true),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentTransactions", x => x.Id);
                    table.CheckConstraint("CK_PaymentTransactions_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_PaymentTransactions_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PaymentTransactions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ShippingMethods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    FreeShippingThreshold = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: true),
                    MinDeliveryDays = table.Column<int>(type: "int", nullable: false),
                    MaxDeliveryDays = table.Column<int>(type: "int", nullable: false),
                    RequiresTimeSlot = table.Column<bool>(type: "bit", nullable: false),
                    SupportsCashOnDelivery = table.Column<bool>(type: "bit", nullable: false),
                    ProvinceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingMethods", x => x.Id);
                    table.CheckConstraint("CK_ShippingMethods_Values", "[Cost] >= 0 AND [MinDeliveryDays] >= 0 AND [MaxDeliveryDays] >= [MinDeliveryDays]");
                    table.ForeignKey(
                        name: "FK_ShippingMethods_Locations_ProvinceId",
                        column: x => x.ProvinceId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SiteFeatures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IconKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LinkUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteFeatures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StoreSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Tagline = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SiteDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LogoUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FaviconUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SupportPhone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SupportEmail = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PostalCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    WorkingHours = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MapEmbedUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    InstagramUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TelegramUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    WhatsAppUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LinkedinUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TwitterUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AparatUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    YoutubeUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FooterTitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    FooterSubtitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    FooterText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CopyrightText = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    FreeShippingThreshold = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    MinimumOrderAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    DeliveryDaysAhead = table.Column<int>(type: "int", nullable: false),
                    DeliveryOffDays = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SameDayLeadMinutes = table.Column<int>(type: "int", nullable: false),
                    UnpaidOrderTimeoutMinutes = table.Column<int>(type: "int", nullable: false),
                    AllowCustomerCancellation = table.Column<bool>(type: "bit", nullable: false),
                    IsOnlinePaymentEnabled = table.Column<bool>(type: "bit", nullable: false),
                    IsCashOnDeliveryEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CashOnDeliveryMaxAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    ZarinPalMerchantId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    ZarinPalSandbox = table.Column<bool>(type: "bit", nullable: false),
                    IsWalletEnabled = table.Column<bool>(type: "bit", nullable: false),
                    IsWalletPaymentEnabled = table.Column<bool>(type: "bit", nullable: false),
                    IsWalletTopUpEnabled = table.Column<bool>(type: "bit", nullable: false),
                    IsWithdrawalEnabled = table.Column<bool>(type: "bit", nullable: false),
                    WalletMinTopUpAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    WalletMaxTopUpAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    WalletMinWithdrawalAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    TitleSeparator = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DefaultMetaKeywords = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DefaultOgImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    GoogleSiteVerification = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BingSiteVerification = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RobotsTxtExtra = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    OrganizationLegalName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreSettings", x => x.Id);
                    table.CheckConstraint("CK_StoreSettings_Values", "[FreeShippingThreshold] >= 0 AND [MinimumOrderAmount] >= 0 AND [CashOnDeliveryMaxAmount] >= 0 AND [WalletMinTopUpAmount] >= 0 AND [WalletMaxTopUpAmount] >= [WalletMinTopUpAmount] AND [WalletMinWithdrawalAmount] >= 0 AND [UnpaidOrderTimeoutMinutes] > 0 AND [DeliveryDaysAhead] > 0");
                });

            migrationBuilder.CreateTable(
                name: "TrustBadges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    LinkUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustBadges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserRefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FamilyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsPersistent = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedReason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReplacedByTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserRefreshTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WalletTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PaymentTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WithdrawalRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletTransactions", x => x.Id);
                    table.CheckConstraint("CK_WalletTransactions_Values", "[Amount] <> 0 AND [BalanceAfter] >= 0");
                    table.ForeignKey(
                        name: "FK_WalletTransactions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WithdrawalRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Iban = table.Column<string>(type: "nvarchar(26)", maxLength: 26, nullable: false),
                    AccountHolder = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AdminNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    BankTrackingNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WithdrawalRequests", x => x.Id);
                    table.CheckConstraint("CK_WithdrawalRequests_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_WithdrawalRequests_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryTimeSlots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShippingMethodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryTimeSlots", x => x.Id);
                    table.CheckConstraint("CK_DeliveryTimeSlots_Values", "[EndTime] > [StartTime] AND ([Capacity] IS NULL OR [Capacity] > 0)");
                    table.ForeignKey(
                        name: "FK_DeliveryTimeSlots_ShippingMethods_ShippingMethodId",
                        column: x => x.ShippingMethodId,
                        principalTable: "ShippingMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_WalletBalance",
                table: "Users",
                sql: "[WalletBalance] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_DeliveryTimeSlotId_RequestedDeliveryDate",
                table: "Orders",
                columns: new[] { "DeliveryTimeSlotId", "RequestedDeliveryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PaymentStatus_PaymentDeadline",
                table: "Orders",
                columns: new[] { "PaymentStatus", "PaymentDeadline" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Amounts",
                table: "Orders",
                sql: "[SubtotalAmount] >= 0 AND [DiscountAmount] >= 0 AND [ShippingAmount] >= 0 AND [PayableAmount] >= 0 AND [RefundedAmount] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_FaqItems_CategoryId",
                table: "FaqItems",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentPages_IsActive_ShowInFooter_DisplayOrder",
                table: "ContentPages",
                columns: new[] { "IsActive", "ShowInFooter", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentPages_PageKey",
                table: "ContentPages",
                column: "PageKey");

            migrationBuilder.CreateIndex(
                name: "IX_ContentPages_Slug",
                table: "ContentPages",
                column: "Slug",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTimeSlots_ShippingMethodId_DisplayOrder",
                table: "DeliveryTimeSlots",
                columns: new[] { "ShippingMethodId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_FaqCategories_Slug",
                table: "FaqCategories",
                column: "Slug",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_HomeSections_CategoryId",
                table: "HomeSections",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_HomeSections_IsActive_DisplayOrder",
                table: "HomeSections",
                columns: new[] { "IsActive", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_Authority",
                table: "PaymentTransactions",
                column: "Authority",
                unique: true,
                filter: "[Authority] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_OrderId",
                table: "PaymentTransactions",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_Status",
                table: "PaymentTransactions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_UserId_CreatedAt",
                table: "PaymentTransactions",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ShippingMethods_Code",
                table: "ShippingMethods",
                column: "Code",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingMethods_IsActive_DisplayOrder",
                table: "ShippingMethods",
                columns: new[] { "IsActive", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ShippingMethods_ProvinceId",
                table: "ShippingMethods",
                column: "ProvinceId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteFeatures_IsActive_DisplayOrder",
                table: "SiteFeatures",
                columns: new[] { "IsActive", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustBadges_IsActive_DisplayOrder",
                table: "TrustBadges",
                columns: new[] { "IsActive", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_UserRefreshTokens_ExpiresAt",
                table: "UserRefreshTokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_UserRefreshTokens_FamilyId",
                table: "UserRefreshTokens",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRefreshTokens_TokenHash",
                table: "UserRefreshTokens",
                column: "TokenHash",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_UserRefreshTokens_UserId_RevokedAt",
                table: "UserRefreshTokens",
                columns: new[] { "UserId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_OrderId",
                table: "WalletTransactions",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_PaymentTransactionId",
                table: "WalletTransactions",
                column: "PaymentTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_UserId_CreatedAt",
                table: "WalletTransactions",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalRequests_Status",
                table: "WithdrawalRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalRequests_UserId_Status",
                table: "WithdrawalRequests",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_FaqItems_FaqCategories_CategoryId",
                table: "FaqItems",
                column: "CategoryId",
                principalTable: "FaqCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FaqItems_FaqCategories_CategoryId",
                table: "FaqItems");

            migrationBuilder.DropTable(
                name: "ContentPages");

            migrationBuilder.DropTable(
                name: "DeliveryTimeSlots");

            migrationBuilder.DropTable(
                name: "FaqCategories");

            migrationBuilder.DropTable(
                name: "HomeSections");

            migrationBuilder.DropTable(
                name: "PaymentTransactions");

            migrationBuilder.DropTable(
                name: "SiteFeatures");

            migrationBuilder.DropTable(
                name: "StoreSettings");

            migrationBuilder.DropTable(
                name: "TrustBadges");

            migrationBuilder.DropTable(
                name: "UserRefreshTokens");

            migrationBuilder.DropTable(
                name: "WalletTransactions");

            migrationBuilder.DropTable(
                name: "WithdrawalRequests");

            migrationBuilder.DropTable(
                name: "ShippingMethods");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_WalletBalance",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Orders_DeliveryTimeSlotId_RequestedDeliveryDate",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_PaymentStatus_PaymentDeadline",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Amounts",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_FaqItems_CategoryId",
                table: "FaqItems");

            migrationBuilder.DropColumn(
                name: "FailedLoginCount",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LockoutEndAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "WalletBalance",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CancelledBy",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DeliveryTimeSlotId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DeliveryTimeSlotTitle",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentDeadline",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RefundedAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RefundedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingMethodId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "FaqItems");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Amounts",
                table: "Orders",
                sql: "[SubtotalAmount] >= 0 AND [DiscountAmount] >= 0 AND [ShippingAmount] >= 0 AND [PayableAmount] >= 0");
        }
    }
}
