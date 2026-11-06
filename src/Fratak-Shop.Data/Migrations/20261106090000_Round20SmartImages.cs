using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <summary>
    /// دور بیستم (دست‌نویس، چون CI امکان ساخت خودکار مهاجرت را ندارد): جدول SmartImageFeedbacks.
    /// <para>هر عکسی که کارشناس برای یک محصول تأیید یا رد می‌کند اینجا ثبت می‌شود: ردشده‌ها دیگر برای همان محصول پیشنهاد نمی‌شوند
    /// (با آدرس یا اثر انگشت تصویری) و نسبت تأیید هر منبع، ترتیب منابع عکس‌یاب را تعیین می‌کند.</para>
    /// <para>هیچ ستون جدول‌های قبلی تغییر نمی‌کند و کلید خارجی ندارد (حذف محصول هرگز گیر نمی‌کند).</para>
    /// </summary>
    public partial class Round20SmartImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SmartImageFeedbacks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Approved = table.Column<bool>(type: "bit", nullable: false),
                    Hash = table.Column<long>(type: "bigint", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UrlKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartImageFeedbacks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SmartImageFeedbacks_ProductId",
                table: "SmartImageFeedbacks",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SmartImageFeedbacks");
        }
    }
}
