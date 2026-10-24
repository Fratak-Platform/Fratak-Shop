using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round4Features : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "InvoiceSerials");

            migrationBuilder.AlterColumn<string>(
                name: "Iban",
                table: "WithdrawalRequests",
                type: "nvarchar(26)",
                maxLength: 26,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(26)",
                oldMaxLength: 26);

            migrationBuilder.AddColumn<string>(
                name: "AccountNumber",
                table: "WithdrawalRequests",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardNumber",
                table: "WithdrawalRequests",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BonusAmount",
                table: "WalletTransactions",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BonusBalanceAfter",
                table: "WalletTransactions",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "WalletBonusBalance",
                table: "Users",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "AbandonedCartAfterMinutes",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<int>(
                name: "AbandonedCartExpireDays",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<decimal>(
                name: "AbandonedCartMinAmount",
                table: "StoreSettings",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceNumberPrefix",
                table: "StoreSettings",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true,
                defaultValue: "INV");

            migrationBuilder.AddColumn<bool>(
                name: "IsAbandonedCartEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsInstantSearchEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPwaInstallPromptEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReviewImageEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReviewOnlyForBuyers",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LoyaltyProgramDescription",
                table: "StoreSettings",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LoyaltyReferralDescription",
                table: "StoreSettings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PwaInstallPromptAfterPages",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "PwaInstallPromptSnoozeDays",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 14);

            migrationBuilder.AddColumn<string>(
                name: "PwaInstallPromptText",
                table: "StoreSettings",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PwaInstallPromptTitle",
                table: "StoreSettings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReviewMaxImageSizeKb",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 2048);

            migrationBuilder.AddColumn<int>(
                name: "ReviewMaxImages",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "WalletMaxPendingWithdrawals",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<decimal>(
                name: "WalletMaxWithdrawalAmount",
                table: "StoreSettings",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "OnlyListedLocations",
                table: "ShippingMethods",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PricingMode",
                table: "ShippingMethods",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "PurchasePrice",
                table: "Products",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowWithoutCategory",
                table: "ProductFilterAttributes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Audience",
                table: "PageAlerts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "PageAlerts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDismissible",
                table: "PageAlerts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LinkText",
                table: "PageAlerts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkUrl",
                table: "PageAlerts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProductId",
                table: "PageAlerts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InvoiceIssuedAt",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceNumber",
                table: "Orders",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RefundedBonusAmount",
                table: "Orders",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "WalletBonusAmount",
                table: "Orders",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCostPrice",
                table: "OrderItems",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsVerifiedPurchase",
                table: "Comments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastActivityAt",
                table: "Carts",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AbandonedCartReminderSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StepNumber = table.Column<int>(type: "int", nullable: false),
                    DelayMinutes = table.Column<int>(type: "int", nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    SmsText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IncludeCoupon = table.Column<bool>(type: "bit", nullable: false),
                    CouponType = table.Column<int>(type: "int", nullable: false),
                    CouponValue = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    CouponMaxDiscountAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: true),
                    CouponMinOrderAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    CouponValidDays = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbandonedCartReminderSteps", x => x.Id);
                    table.CheckConstraint("CK_AbandonedCartReminderSteps_Values", "[StepNumber] > 0 AND [DelayMinutes] > 0 AND [CouponValue] >= 0 AND [CouponMinOrderAmount] >= 0 AND [CouponValidDays] > 0 AND ([IncludeCoupon] = 0 OR [CouponType] <> 1 OR ([CouponValue] > 0 AND [CouponValue] <= 100))");
                });

            migrationBuilder.CreateTable(
                name: "AbandonedCarts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CartId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AbandonedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CartAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    RemindersSent = table.Column<int>(type: "int", nullable: false),
                    LastReminderAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CouponId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CouponCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RecoveredOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecoveredAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbandonedCarts", x => x.Id);
                    table.CheckConstraint("CK_AbandonedCarts_Values", "[CartAmount] >= 0 AND [RecoveredAmount] >= 0 AND [ItemCount] >= 0 AND [RemindersSent] >= 0");
                    table.ForeignKey(
                        name: "FK_AbandonedCarts_Carts_CartId",
                        column: x => x.CartId,
                        principalTable: "Carts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AbandonedCarts_Coupons_CouponId",
                        column: x => x.CouponId,
                        principalTable: "Coupons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AbandonedCarts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommentImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SizeBytes = table.Column<int>(type: "int", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentImages", x => x.Id);
                    table.CheckConstraint("CK_CommentImages_Size", "[SizeBytes] > 0");
                    table.ForeignKey(
                        name: "FK_CommentImages_Comments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "Comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShippingRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShippingMethodId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProvinceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Cost = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: true),
                    FreeShippingThreshold = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: true),
                    ExtraDeliveryDays = table.Column<int>(type: "int", nullable: false),
                    IsBlocked = table.Column<bool>(type: "bit", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingRates", x => x.Id);
                    table.CheckConstraint("CK_ShippingRates_Values", "([ProvinceId] IS NOT NULL OR [CityId] IS NOT NULL) AND ([Cost] IS NULL OR [Cost] >= 0) AND ([FreeShippingThreshold] IS NULL OR [FreeShippingThreshold] >= 0) AND [ExtraDeliveryDays] >= 0");
                    table.ForeignKey(
                        name: "FK_ShippingRates_Locations_CityId",
                        column: x => x.CityId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShippingRates_Locations_ProvinceId",
                        column: x => x.ProvinceId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShippingRates_ShippingMethods_ShippingMethodId",
                        column: x => x.ShippingMethodId,
                        principalTable: "ShippingMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_WithdrawalRequests_Destination",
                table: "WithdrawalRequests",
                sql: "[Iban] IS NOT NULL OR [CardNumber] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WalletTransactions_Bonus",
                table: "WalletTransactions",
                sql: "[BonusBalanceAfter] >= 0 AND [BonusBalanceAfter] <= [BalanceAfter] AND ABS([BonusAmount]) <= ABS([Amount])");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_WalletBonusBalance",
                table: "Users",
                sql: "[WalletBonusBalance] >= 0 AND [WalletBonusBalance] <= [WalletBalance]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StoreSettings_Round4",
                table: "StoreSettings",
                sql: "[WalletMaxWithdrawalAmount] >= 0 AND [WalletMaxPendingWithdrawals] > 0 AND [AbandonedCartAfterMinutes] > 0 AND [AbandonedCartMinAmount] >= 0 AND [AbandonedCartExpireDays] > 0 AND [ReviewMaxImages] >= 0 AND [ReviewMaxImages] <= 10 AND [ReviewMaxImageSizeKb] > 0 AND [PwaInstallPromptAfterPages] >= 0 AND [PwaInstallPromptSnoozeDays] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_PurchasePrice",
                table: "Products",
                sql: "[PurchasePrice] IS NULL OR [PurchasePrice] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_PageAlerts_CategoryId",
                table: "PageAlerts",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_PageAlerts_ProductId",
                table: "PageAlerts",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_InvoiceNumber",
                table: "Orders",
                column: "InvoiceNumber",
                unique: true,
                filter: "[InvoiceNumber] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_WalletBonus",
                table: "Orders",
                sql: "[WalletBonusAmount] >= 0 AND [WalletBonusAmount] <= [PayableAmount] AND [RefundedBonusAmount] >= 0 AND [RefundedBonusAmount] <= [RefundedAmount]");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_ProductId_UserId_Type",
                table: "Comments",
                columns: new[] { "ProductId", "UserId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_Carts_LastActivityAt",
                table: "Carts",
                column: "LastActivityAt");

            migrationBuilder.CreateIndex(
                name: "IX_AbandonedCartReminderSteps_IsActive_StepNumber",
                table: "AbandonedCartReminderSteps",
                columns: new[] { "IsActive", "StepNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_AbandonedCarts_CartId_Status",
                table: "AbandonedCarts",
                columns: new[] { "CartId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AbandonedCarts_CouponId",
                table: "AbandonedCarts",
                column: "CouponId");

            migrationBuilder.CreateIndex(
                name: "IX_AbandonedCarts_Status_AbandonedAt",
                table: "AbandonedCarts",
                columns: new[] { "Status", "AbandonedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AbandonedCarts_UserId",
                table: "AbandonedCarts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CommentImages_CommentId_DisplayOrder",
                table: "CommentImages",
                columns: new[] { "CommentId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CommentImages_FileName",
                table: "CommentImages",
                column: "FileName",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingRates_CityId",
                table: "ShippingRates",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingRates_ProvinceId",
                table: "ShippingRates",
                column: "ProvinceId");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingRates_ShippingMethodId_ProvinceId_CityId",
                table: "ShippingRates",
                columns: new[] { "ShippingMethodId", "ProvinceId", "CityId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbandonedCartReminderSteps");

            migrationBuilder.DropTable(
                name: "AbandonedCarts");

            migrationBuilder.DropTable(
                name: "CommentImages");

            migrationBuilder.DropTable(
                name: "ShippingRates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WithdrawalRequests_Destination",
                table: "WithdrawalRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WalletTransactions_Bonus",
                table: "WalletTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_WalletBonusBalance",
                table: "Users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StoreSettings_Round4",
                table: "StoreSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_PurchasePrice",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_PageAlerts_CategoryId",
                table: "PageAlerts");

            migrationBuilder.DropIndex(
                name: "IX_PageAlerts_ProductId",
                table: "PageAlerts");

            migrationBuilder.DropIndex(
                name: "IX_Orders_InvoiceNumber",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_WalletBonus",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Comments_ProductId_UserId_Type",
                table: "Comments");

            migrationBuilder.DropIndex(
                name: "IX_Carts_LastActivityAt",
                table: "Carts");

            migrationBuilder.DropColumn(
                name: "AccountNumber",
                table: "WithdrawalRequests");

            migrationBuilder.DropColumn(
                name: "CardNumber",
                table: "WithdrawalRequests");

            migrationBuilder.DropColumn(
                name: "BonusAmount",
                table: "WalletTransactions");

            migrationBuilder.DropColumn(
                name: "BonusBalanceAfter",
                table: "WalletTransactions");

            migrationBuilder.DropColumn(
                name: "WalletBonusBalance",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AbandonedCartAfterMinutes",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "AbandonedCartExpireDays",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "AbandonedCartMinAmount",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "InvoiceNumberPrefix",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsAbandonedCartEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsInstantSearchEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsPwaInstallPromptEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsReviewImageEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsReviewOnlyForBuyers",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "LoyaltyProgramDescription",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "LoyaltyReferralDescription",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "PwaInstallPromptAfterPages",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "PwaInstallPromptSnoozeDays",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "PwaInstallPromptText",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "PwaInstallPromptTitle",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReviewMaxImageSizeKb",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReviewMaxImages",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "WalletMaxPendingWithdrawals",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "WalletMaxWithdrawalAmount",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "OnlyListedLocations",
                table: "ShippingMethods");

            migrationBuilder.DropColumn(
                name: "PricingMode",
                table: "ShippingMethods");

            migrationBuilder.DropColumn(
                name: "PurchasePrice",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ShowWithoutCategory",
                table: "ProductFilterAttributes");

            migrationBuilder.DropColumn(
                name: "Audience",
                table: "PageAlerts");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "PageAlerts");

            migrationBuilder.DropColumn(
                name: "IsDismissible",
                table: "PageAlerts");

            migrationBuilder.DropColumn(
                name: "LinkText",
                table: "PageAlerts");

            migrationBuilder.DropColumn(
                name: "LinkUrl",
                table: "PageAlerts");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "PageAlerts");

            migrationBuilder.DropColumn(
                name: "InvoiceIssuedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "InvoiceNumber",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RefundedBonusAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "WalletBonusAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "UnitCostPrice",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "IsVerifiedPurchase",
                table: "Comments");

            migrationBuilder.DropColumn(
                name: "LastActivityAt",
                table: "Carts");

            migrationBuilder.DropSequence(
                name: "InvoiceSerials");

            migrationBuilder.AlterColumn<string>(
                name: "Iban",
                table: "WithdrawalRequests",
                type: "nvarchar(26)",
                maxLength: 26,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(26)",
                oldMaxLength: 26,
                oldNullable: true);
        }
    }
}
