using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <summary>
    /// دور ۳۱ (دست‌نویس):
    /// <list type="bullet">
    /// <item>Products.SupplierCategory – مسیر دسته‌ای که تأمین‌کننده در صفحهٔ کالای خودش گذاشته (قانون پروژه: سیستم دسته را نفهمید ←
    /// لینک تأمین‌کننده خوانده می‌شود). فقط شاهد دسته‌بندی هوشمند است.</item>
    /// <item>Products.SupplierCategoryAt – آخرین بار خواندن صفحهٔ تأمین‌کننده برای دسته (هر کالا حداکثر هر ۱۴ روز).</item>
    /// <item>صف دوبارهٔ دوربری عکس‌های اصلی که قبلاً رد شده بودند (فقط داده؛ ساختار جدول عوض نمی‌شود).</item>
    /// </list>
    /// </summary>
    public partial class Round31 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SupplierCategory",
                table: "Products",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SupplierCategoryAt",
                table: "Products",
                type: "datetimeoffset",
                nullable: true);

            // دور ۳۱ (#۸): دوربری حالا عکس واترمارک‌دار/نوشته پشت کالا و قاب‌دار را هم امتحان می‌کند؛ عکس‌های اصلی‌ای که قبلاً
            // فقط «امتحان شد» (بیت ۱۲۸) خورده‌اند و هنوز نسخهٔ بدون زمینه ندارند (بیت ۶۴) و آدم‌دار نیستند (بیت ۴) یک بار دیگر صف می‌شوند.
            migrationBuilder.Sql(
                "UPDATE [ProductImages] SET [SubjectFlags] = [SubjectFlags] - 128 " +
                "WHERE [IsPrimary] = 1 AND [SubjectFlags] IS NOT NULL AND ([SubjectFlags] & 128) <> 0 AND ([SubjectFlags] & 64) = 0 AND ([SubjectFlags] & 4) = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SupplierCategory",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SupplierCategoryAt",
                table: "Products");
        }
    }
}
