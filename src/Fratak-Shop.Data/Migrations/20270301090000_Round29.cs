using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <summary>
    /// دور ۲۹ (دست‌نویس):
    /// <list type="bullet">
    /// <item>Notifications.UserDeletable – مشتری فقط اعلانی را می‌تواند حذف کند که با این پرچم فرستاده شده (پیش‌فرض false = فقط مدیر).</item>
    /// <item>NotificationReads.HiddenAt – پیام همگانی که مشتری از فهرست خودش برداشته (برای بقیه می‌ماند).</item>
    /// <item>Banners.IsPinned – بنر سنجاق‌شده همیشه نمایش داده می‌شود و در چرخش نوبتی بیرون نمی‌رود.</item>
    /// <item>Banners.MobileImageName – بنر دیگر نسخهٔ موبایل جدا ندارد؛ مقدارهای قبلی خالی می‌شوند (ستون برای سازگاری می‌ماند).</item>
    /// <item>LandingPages.CodeMode/CodeHtml/CodeCss/CodeJs – «حالت کد» صفحهٔ کمپین (HTML/CSS/JS خام داخل iframe ایزوله).</item>
    /// <item>ProductVideoSuggestions – ویدیوهای پیشنهادی هر کالا (فقط پیشنهاد؛ مدیر بررسی و دانلود می‌کند).</item>
    /// </list>
    /// </summary>
    public partial class Round29 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "UserDeletable",
                table: "Notifications",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HiddenAt",
                table: "NotificationReads",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPinned",
                table: "Banners",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CodeMode",
                table: "LandingPages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CodeHtml",
                table: "LandingPages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CodeCss",
                table: "LandingPages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CodeJs",
                table: "LandingPages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductVideoSuggestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ThumbnailUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Score = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVideoSuggestions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVideoSuggestions_ProductId_PageUrl",
                table: "ProductVideoSuggestions",
                columns: new[] { "ProductId", "PageUrl" },
                unique: true,
                filter: "[IsDeleted] = 0");

            // پیام‌های همگانیِ قبلی (غیر امنیتی) را مشتری می‌تواند از فهرستش بردارد؛ اعلان‌های شخصی قبلی «فقط مدیر» می‌مانند.
            migrationBuilder.Sql("UPDATE [Notifications] SET [UserDeletable] = 1 WHERE [UserId] IS NULL;");

            // بنر بدون نسخهٔ موبایل جدا: همان تصویر اصلی در موبایل بهتر چیده می‌شود.
            migrationBuilder.Sql("UPDATE [Banners] SET [MobileImageName] = NULL WHERE [MobileImageName] IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "UserDeletable", table: "Notifications");
            migrationBuilder.DropColumn(name: "HiddenAt", table: "NotificationReads");
            migrationBuilder.DropColumn(name: "IsPinned", table: "Banners");
            migrationBuilder.DropTable(name: "ProductVideoSuggestions");
            migrationBuilder.DropColumn(name: "CodeMode", table: "LandingPages");
            migrationBuilder.DropColumn(name: "CodeHtml", table: "LandingPages");
            migrationBuilder.DropColumn(name: "CodeCss", table: "LandingPages");
            migrationBuilder.DropColumn(name: "CodeJs", table: "LandingPages");
        }
    }
}
