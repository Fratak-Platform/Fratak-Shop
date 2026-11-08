using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <summary>
    /// دور ۲۲ (دست‌نویس): ورود هوشمند کالا و تصویرهای پیشنهادی دسته.
    /// <list type="bullet">
    /// <item>Products.SmartIntakeAt: زمان «ورود هوشمند» (دسته‌بندی + صف تکمیل). کالاهای موجود همین الان «انجام‌شده» علامت می‌خورند
    /// تا بعد از انتشار، صف هوشمند با هزاران کالای قدیمی پر نشود (آن‌ها را تکمیل شبانه و دسته‌بندی دوره‌ای پوشش می‌دهند).</item>
    /// <item>Products.SourceCategory: دستهٔ کالا در سایت مبدأ/فروشگاه‌های بزرگ (فقط شاهد دسته‌بندی؛ نمایش داده نمی‌شود).</item>
    /// <item>Products.CategorySource: چه کسی دسته را گذاشته (۰ ادمین/قدیمی، ۱ هوشمند دقیق، ۲ هوشمند کلی).</item>
    /// <item>Categories.ImageOptionsJson: تا ۴ تصویر پیشنهادی دسته.</item>
    /// </list>
    /// Down ستون‌ها و نمایه را حذف می‌کند.
    /// </summary>
    public partial class Round22Intake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(name: "SmartIntakeAt", table: "Products", type: "datetimeoffset", nullable: true);
            migrationBuilder.AddColumn<string>(name: "SourceCategory", table: "Products", type: "nvarchar(300)", maxLength: 300, nullable: true);
            migrationBuilder.AddColumn<int>(name: "CategorySource", table: "Products", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<string>(name: "ImageOptionsJson", table: "Categories", type: "nvarchar(2000)", maxLength: 2000, nullable: true);

            // کالاهای موجود «ورود هوشمند» لازم ندارند (فقط کالاهای ساخته‌شده از این به بعد).
            migrationBuilder.Sql("UPDATE [Products] SET [SmartIntakeAt] = SYSDATETIMEOFFSET() WHERE [SmartIntakeAt] IS NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_Products_SmartIntakeAt",
                table: "Products",
                column: "SmartIntakeAt",
                filter: "[SmartIntakeAt] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Products_SmartIntakeAt", table: "Products");
            migrationBuilder.DropColumn(name: "SmartIntakeAt", table: "Products");
            migrationBuilder.DropColumn(name: "SourceCategory", table: "Products");
            migrationBuilder.DropColumn(name: "CategorySource", table: "Products");
            migrationBuilder.DropColumn(name: "ImageOptionsJson", table: "Categories");
        }
    }
}
