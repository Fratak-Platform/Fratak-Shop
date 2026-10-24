using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round5Features : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Latitude",
                table: "UserAddresses",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitude",
                table: "UserAddresses",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CaptchaAfterFailedAttempts",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "DefaultTheme",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "GiftCardMaxAmount",
                table: "StoreSettings",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 10000000m);

            migrationBuilder.AddColumn<decimal>(
                name: "GiftCardMinAmount",
                table: "StoreSettings",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 100000m);

            migrationBuilder.AddColumn<string>(
                name: "GiftCardPresetAmounts",
                table: "StoreSettings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                defaultValue: "200000,500000,1000000,2000000");

            migrationBuilder.AddColumn<string>(
                name: "GiftCardTerms",
                table: "StoreSettings",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GiftCardValidDays",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 365);

            migrationBuilder.AddColumn<int>(
                name: "ImageQuality",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 80);

            migrationBuilder.AddColumn<bool>(
                name: "IsAddressMapEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCaptchaEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCommentVotingEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCustomerAnswerEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDarkModeEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDidYouMeanEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsGiftCardCreditWithdrawable",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsGiftCardEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsImageOptimizationEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsNewDeviceAlertEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsOrderFeedbackEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPageCacheEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPopularSearchesEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPriceHistoryEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsProductAlertEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsProductVideoEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReturnEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSaveForLaterEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSearchSynonymsEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSmartCompareEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTicketEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsWebPushEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "MapAttribution",
                table: "StoreSettings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                defaultValue: "© OpenStreetMap");

            migrationBuilder.AddColumn<decimal>(
                name: "MapDefaultLatitude",
                table: "StoreSettings",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: false,
                defaultValue: 35.6892m);

            migrationBuilder.AddColumn<decimal>(
                name: "MapDefaultLongitude",
                table: "StoreSettings",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: false,
                defaultValue: 51.3890m);

            migrationBuilder.AddColumn<int>(
                name: "MapDefaultZoom",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 12);

            migrationBuilder.AddColumn<string>(
                name: "MapTileUrl",
                table: "StoreSettings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                defaultValue: "https://tile.openstreetmap.org/{z}/{x}/{y}.png");

            migrationBuilder.AddColumn<int>(
                name: "OrderFeedbackWindowDays",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "PageCacheSeconds",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 300);

            migrationBuilder.AddColumn<int>(
                name: "PopularSearchMinCount",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "PopularSearchesCount",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 8);

            migrationBuilder.AddColumn<int>(
                name: "PriceHistoryDays",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 90);

            migrationBuilder.AddColumn<int>(
                name: "ProductAlertMaxPerUser",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 50);

            migrationBuilder.AddColumn<int>(
                name: "ReferralCookieDays",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<string>(
                name: "ReferralShareText",
                table: "StoreSettings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReturnMaxImages",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<string>(
                name: "ReturnPolicyText",
                table: "StoreSettings",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReturnWindowDays",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<int>(
                name: "TicketMaxAttachmentSizeKb",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 2048);

            migrationBuilder.AddColumn<int>(
                name: "TicketMaxAttachments",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "TicketMaxOpenPerUser",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<string>(
                name: "VapidPrivateKey",
                table: "StoreSettings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VapidPublicKey",
                table: "StoreSettings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VapidSubject",
                table: "StoreSettings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "XSiteHandle",
                table: "StoreSettings",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HitCount",
                table: "SeoRedirects",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastHitAt",
                table: "SeoRedirects",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Gtin",
                table: "Products",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Mpn",
                table: "Products",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeliveredAt",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShippingLatitude",
                table: "Orders",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShippingLongitude",
                table: "Orders",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SendPush",
                table: "NotificationTemplates",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "HelpfulCount",
                table: "Comments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "NotHelpfulCount",
                table: "Comments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CommentVotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsHelpful = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommentVotes_Comments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "Comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CommentVotes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GiftCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CodeLast4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    ProtectedCode = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PurchaserUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentMethod = table.Column<int>(type: "int", nullable: false),
                    PaymentTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecipientName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RedeemedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RedeemedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RedeemedAsWithdrawable = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiftCards", x => x.Id);
                    table.CheckConstraint("CK_GiftCards_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_GiftCards_PaymentTransactions_PaymentTransactionId",
                        column: x => x.PaymentTransactionId,
                        principalTable: "PaymentTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GiftCards_Users_PurchaserUserId",
                        column: x => x.PurchaserUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GiftCards_Users_RedeemedByUserId",
                        column: x => x.RedeemedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderFeedbacks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OverallRating = table.Column<byte>(type: "tinyint", nullable: false),
                    DeliveryRating = table.Column<byte>(type: "tinyint", nullable: false),
                    PackagingRating = table.Column<byte>(type: "tinyint", nullable: false),
                    ProductMatchRating = table.Column<byte>(type: "tinyint", nullable: false),
                    WouldRecommend = table.Column<bool>(type: "bit", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderFeedbacks", x => x.Id);
                    table.CheckConstraint("CK_OrderFeedbacks_Ratings", "[OverallRating] BETWEEN 1 AND 5 AND [DeliveryRating] BETWEEN 1 AND 5 AND [PackagingRating] BETWEEN 1 AND 5 AND [ProductMatchRating] BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_OrderFeedbacks_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderFeedbacks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    PriceAtSubscribe = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    TargetPrice = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    NotifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NotifiedPrice = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductAlerts", x => x.Id);
                    table.CheckConstraint("CK_ProductAlerts_Values", "[PriceAtSubscribe] >= 0 AND ([TargetPrice] IS NULL OR [TargetPrice] > 0)");
                    table.ForeignKey(
                        name: "FK_ProductAlerts_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductAlerts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductPriceHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    FinalPrice = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    IsInStock = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductPriceHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductPriceHistories_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductVideos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Provider = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    PosterImage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DurationSeconds = table.Column<int>(type: "int", nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVideos", x => x.Id);
                    table.CheckConstraint("CK_ProductVideos_Provider", "[Provider] BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "FK_ProductVideos_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReturnReasons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Hint = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RequiresImage = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnReasons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SavedCartItems",
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
                    table.PrimaryKey("PK_SavedCartItems", x => x.Id);
                    table.CheckConstraint("CK_SavedCartItems_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_SavedCartItems_Carts_CartId",
                        column: x => x.CartId,
                        principalTable: "Carts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SavedCartItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SearchQueryLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NormalizedQuery = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Query = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SearchCount = table.Column<int>(type: "int", nullable: false),
                    LastResultCount = table.Column<int>(type: "int", nullable: false),
                    LastSearchedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsPinned = table.Column<bool>(type: "bit", nullable: false),
                    IsHidden = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchQueryLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SearchSynonyms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Terms = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchSynonyms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeoNotFoundLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Path = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    LastReferrer = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    HitCount = table.Column<int>(type: "int", nullable: false),
                    LastHitAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsResolved = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeoNotFoundLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SupportDepartments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportDepartments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserKnownDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DeviceName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    LastIp = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserKnownDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserKnownDevices_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebPushSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Endpoint = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: false),
                    EndpointHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    P256dh = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Auth = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UserAgent = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    LastSuccessAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailureCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebPushSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebPushSubscriptions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReturnRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReasonTitle = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestedAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    ApprovedAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: true),
                    RefundedAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    RefundedBonusAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    AdminNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReturnTrackingCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RefundedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnRequests", x => x.Id);
                    table.CheckConstraint("CK_ReturnRequests_Amounts", "[RequestedAmount] >= 0 AND ([ApprovedAmount] IS NULL OR [ApprovedAmount] >= 0) AND [RefundedAmount] >= 0 AND [RefundedBonusAmount] >= 0 AND [RefundedBonusAmount] <= [RefundedAmount]");
                    table.ForeignKey(
                        name: "FK_ReturnRequests_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnRequests_ReturnReasons_ReasonId",
                        column: x => x.ReasonId,
                        principalTable: "ReturnReasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ReturnRequests_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupportTickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TicketNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    HasUnreadReply = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportTickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportTickets_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportTickets_SupportDepartments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "SupportDepartments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportTickets_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReturnRequestImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReturnRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SizeBytes = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnRequestImages", x => x.Id);
                    table.CheckConstraint("CK_ReturnRequestImages_Size", "[SizeBytes] > 0");
                    table.ForeignKey(
                        name: "FK_ReturnRequestImages_ReturnRequests_ReturnRequestId",
                        column: x => x.ReturnRequestId,
                        principalTable: "ReturnRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReturnRequestItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReturnRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitRefundAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnRequestItems", x => x.Id);
                    table.CheckConstraint("CK_ReturnRequestItems_Values", "[Quantity] > 0 AND [UnitRefundAmount] >= 0");
                    table.ForeignKey(
                        name: "FK_ReturnRequestItems_OrderItems_OrderItemId",
                        column: x => x.OrderItemId,
                        principalTable: "OrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnRequestItems_ReturnRequests_ReturnRequestId",
                        column: x => x.ReturnRequestId,
                        principalTable: "ReturnRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupportTicketMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TicketId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsStaff = table.Column<bool>(type: "bit", nullable: false),
                    AuthorName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportTicketMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportTicketMessages_SupportTickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "SupportTickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SupportTicketMessages_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupportTicketAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SizeBytes = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportTicketAttachments", x => x.Id);
                    table.CheckConstraint("CK_SupportTicketAttachments_Size", "[SizeBytes] > 0");
                    table.ForeignKey(
                        name: "FK_SupportTicketAttachments_SupportTicketMessages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "SupportTicketMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserAddresses_Location",
                table: "UserAddresses",
                sql: "([Latitude] IS NULL AND [Longitude] IS NULL) OR ([Latitude] BETWEEN -90 AND 90 AND [Longitude] BETWEEN -180 AND 180)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StoreSettings_Round5",
                table: "StoreSettings",
                sql: "[ProductAlertMaxPerUser] >= 0 AND [GiftCardMinAmount] > 0 AND [GiftCardMaxAmount] >= [GiftCardMinAmount] AND [GiftCardValidDays] >= 0 AND [ReferralCookieDays] >= 0 AND [CaptchaAfterFailedAttempts] >= 0 AND [PriceHistoryDays] > 0 AND [MapDefaultZoom] BETWEEN 1 AND 19 AND [ReturnWindowDays] >= 0 AND [ReturnMaxImages] BETWEEN 0 AND 10 AND [OrderFeedbackWindowDays] >= 0 AND [DefaultTheme] BETWEEN 0 AND 2 AND [PopularSearchesCount] BETWEEN 0 AND 30 AND [PopularSearchMinCount] >= 1 AND [TicketMaxOpenPerUser] >= 1 AND [TicketMaxAttachments] BETWEEN 0 AND 10 AND [TicketMaxAttachmentSizeKb] > 0 AND [ImageQuality] BETWEEN 40 AND 95 AND [PageCacheSeconds] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_CommentVotes_CommentId_UserId",
                table: "CommentVotes",
                columns: new[] { "CommentId", "UserId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CommentVotes_UserId",
                table: "CommentVotes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_GiftCards_CodeHash",
                table: "GiftCards",
                column: "CodeHash",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_GiftCards_PaymentTransactionId",
                table: "GiftCards",
                column: "PaymentTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_GiftCards_PurchaserUserId",
                table: "GiftCards",
                column: "PurchaserUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GiftCards_RedeemedByUserId",
                table: "GiftCards",
                column: "RedeemedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GiftCards_Reference",
                table: "GiftCards",
                column: "Reference",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OrderFeedbacks_OrderId",
                table: "OrderFeedbacks",
                column: "OrderId",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OrderFeedbacks_UserId",
                table: "OrderFeedbacks",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductAlerts_ProductId_IsActive_Type",
                table: "ProductAlerts",
                columns: new[] { "ProductId", "IsActive", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductAlerts_UserId_ProductId_Type",
                table: "ProductAlerts",
                columns: new[] { "UserId", "ProductId", "Type" },
                unique: true,
                filter: "[IsActive] = 1 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ProductPriceHistories_ProductId_CreatedAt",
                table: "ProductPriceHistories",
                columns: new[] { "ProductId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVideos_ProductId_DisplayOrder",
                table: "ProductVideos",
                columns: new[] { "ProductId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequestImages_ReturnRequestId",
                table: "ReturnRequestImages",
                column: "ReturnRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequestItems_OrderItemId",
                table: "ReturnRequestItems",
                column: "OrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequestItems_ReturnRequestId",
                table: "ReturnRequestItems",
                column: "ReturnRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequests_OrderId",
                table: "ReturnRequests",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequests_ReasonId",
                table: "ReturnRequests",
                column: "ReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequests_RequestNumber",
                table: "ReturnRequests",
                column: "RequestNumber",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequests_UserId_Status",
                table: "ReturnRequests",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SavedCartItems_CartId_ProductId",
                table: "SavedCartItems",
                columns: new[] { "CartId", "ProductId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SavedCartItems_ProductId",
                table: "SavedCartItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SearchQueryLogs_IsHidden_SearchCount",
                table: "SearchQueryLogs",
                columns: new[] { "IsHidden", "SearchCount" });

            migrationBuilder.CreateIndex(
                name: "IX_SearchQueryLogs_NormalizedQuery",
                table: "SearchQueryLogs",
                column: "NormalizedQuery",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SeoNotFoundLogs_IsResolved_HitCount",
                table: "SeoNotFoundLogs",
                columns: new[] { "IsResolved", "HitCount" });

            migrationBuilder.CreateIndex(
                name: "IX_SeoNotFoundLogs_Path",
                table: "SeoNotFoundLogs",
                column: "Path",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicketAttachments_FileName",
                table: "SupportTicketAttachments",
                column: "FileName",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicketAttachments_MessageId",
                table: "SupportTicketAttachments",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicketMessages_TicketId_CreatedAt",
                table: "SupportTicketMessages",
                columns: new[] { "TicketId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicketMessages_UserId",
                table: "SupportTicketMessages",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_DepartmentId",
                table: "SupportTickets",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_OrderId",
                table: "SupportTickets",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_Status_LastActivityAt",
                table: "SupportTickets",
                columns: new[] { "Status", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_TicketNumber",
                table: "SupportTickets",
                column: "TicketNumber",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_UserId_Status",
                table: "SupportTickets",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_UserKnownDevices_UserId_DeviceHash",
                table: "UserKnownDevices",
                columns: new[] { "UserId", "DeviceHash" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_WebPushSubscriptions_EndpointHash",
                table: "WebPushSubscriptions",
                column: "EndpointHash",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_WebPushSubscriptions_UserId",
                table: "WebPushSubscriptions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommentVotes");

            migrationBuilder.DropTable(
                name: "GiftCards");

            migrationBuilder.DropTable(
                name: "OrderFeedbacks");

            migrationBuilder.DropTable(
                name: "ProductAlerts");

            migrationBuilder.DropTable(
                name: "ProductPriceHistories");

            migrationBuilder.DropTable(
                name: "ProductVideos");

            migrationBuilder.DropTable(
                name: "ReturnRequestImages");

            migrationBuilder.DropTable(
                name: "ReturnRequestItems");

            migrationBuilder.DropTable(
                name: "SavedCartItems");

            migrationBuilder.DropTable(
                name: "SearchQueryLogs");

            migrationBuilder.DropTable(
                name: "SearchSynonyms");

            migrationBuilder.DropTable(
                name: "SeoNotFoundLogs");

            migrationBuilder.DropTable(
                name: "SupportTicketAttachments");

            migrationBuilder.DropTable(
                name: "UserKnownDevices");

            migrationBuilder.DropTable(
                name: "WebPushSubscriptions");

            migrationBuilder.DropTable(
                name: "ReturnRequests");

            migrationBuilder.DropTable(
                name: "SupportTicketMessages");

            migrationBuilder.DropTable(
                name: "ReturnReasons");

            migrationBuilder.DropTable(
                name: "SupportTickets");

            migrationBuilder.DropTable(
                name: "SupportDepartments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserAddresses_Location",
                table: "UserAddresses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StoreSettings_Round5",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "UserAddresses");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "UserAddresses");

            migrationBuilder.DropColumn(
                name: "CaptchaAfterFailedAttempts",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "DefaultTheme",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "GiftCardMaxAmount",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "GiftCardMinAmount",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "GiftCardPresetAmounts",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "GiftCardTerms",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "GiftCardValidDays",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ImageQuality",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsAddressMapEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsCaptchaEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsCommentVotingEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsCustomerAnswerEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsDarkModeEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsDidYouMeanEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsGiftCardCreditWithdrawable",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsGiftCardEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsImageOptimizationEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsNewDeviceAlertEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsOrderFeedbackEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsPageCacheEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsPopularSearchesEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsPriceHistoryEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsProductAlertEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsProductVideoEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsReturnEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsSaveForLaterEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsSearchSynonymsEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsSmartCompareEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsTicketEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsWebPushEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "MapAttribution",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "MapDefaultLatitude",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "MapDefaultLongitude",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "MapDefaultZoom",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "MapTileUrl",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "OrderFeedbackWindowDays",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "PageCacheSeconds",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "PopularSearchMinCount",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "PopularSearchesCount",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "PriceHistoryDays",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ProductAlertMaxPerUser",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReferralCookieDays",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReferralShareText",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReturnMaxImages",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReturnPolicyText",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReturnWindowDays",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "TicketMaxAttachmentSizeKb",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "TicketMaxAttachments",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "TicketMaxOpenPerUser",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "VapidPrivateKey",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "VapidPublicKey",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "VapidSubject",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "XSiteHandle",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "HitCount",
                table: "SeoRedirects");

            migrationBuilder.DropColumn(
                name: "LastHitAt",
                table: "SeoRedirects");

            migrationBuilder.DropColumn(
                name: "Gtin",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Mpn",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "DeliveredAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingLatitude",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingLongitude",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SendPush",
                table: "NotificationTemplates");

            migrationBuilder.DropColumn(
                name: "HelpfulCount",
                table: "Comments");

            migrationBuilder.DropColumn(
                name: "NotHelpfulCount",
                table: "Comments");
        }
    }
}
