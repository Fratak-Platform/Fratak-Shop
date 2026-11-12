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
        }
    }
}
