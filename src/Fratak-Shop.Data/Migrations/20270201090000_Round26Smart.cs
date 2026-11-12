using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <summary>
    /// دور ۲۶ (دست‌نویس): بنرهای متحرک، دستهٔ رأی بازار، حلقهٔ آماده‌سازی کالا و امضای چندتصویری جست‌وجوی تصویری.
    /// <list type="bullet">
    /// <item>DesignDocs.Animation – حرکت بنر (۰ ثابت، ۱ ورود نرم، ۲ ورود/خروج پیوسته).</item>
    /// <item>Banners.LiveHtml – نسخهٔ زندهٔ (متحرک) بنر که سامانه از طرح می‌سازد.</item>
    /// <item>Products.MarketCategory – دسته‌ای که بیشتر سایت‌های دیگر برای کالا گذاشته‌اند.</item>
    /// <item>Products.SmartReadyAttempts و SmartReadyCheckedAt – شمارش و زمان تلاش‌های «آمادهٔ انتشار».</item>
    /// <item>ProductImageSignatures.Hashes و ImageCount – امضای چند عکس هر کالا (dHash + pHash).</item>
    /// </list>
    /// امضاهای قدیمی با خالی کردن ImageName نامعتبر می‌شوند تا کارگر پس‌زمینه با الگوریتم تازه دوباره بسازد.
    /// Down همه را برمی‌دارد.
    /// </summary>
    public partial class Round26Smart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "Animation",
                table: "DesignDocs",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<string>(
                name: "LiveHtml",
                table: "Banners",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MarketCategory",
                table: "Products",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SmartReadyAttempts",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SmartReadyCheckedAt",
                table: "Products",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "Hashes",
                table: "ProductImageSignatures",
                type: "varbinary(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImageCount",
                table: "ProductImageSignatures",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // امضای قدیمی (فقط عکس اول، بدون pHash) دوباره ساخته شود.
            migrationBuilder.Sql("UPDATE ProductImageSignatures SET ImageName = N''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Animation", table: "DesignDocs");
            migrationBuilder.DropColumn(name: "LiveHtml", table: "Banners");
            migrationBuilder.DropColumn(name: "MarketCategory", table: "Products");
            migrationBuilder.DropColumn(name: "SmartReadyAttempts", table: "Products");
            migrationBuilder.DropColumn(name: "SmartReadyCheckedAt", table: "Products");
            migrationBuilder.DropColumn(name: "Hashes", table: "ProductImageSignatures");
            migrationBuilder.DropColumn(name: "ImageCount", table: "ProductImageSignatures");
        }
    }
}
