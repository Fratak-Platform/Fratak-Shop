using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <summary>
    /// دور ۳۰ (دست‌نویس):
    /// <list type="bullet">
    /// <item>CategoryImageFeedbacks – حافظهٔ یادگیری تصویر دسته: هر تصویری که سیستم برای دسته گذاشته و تصمیم ادمین
    /// (انتخاب = تأیید، حذف = رد) تا فیلتر دفعه‌های بعد بهتر شود.</item>
    /// <item>ProductImageSignatures.Hashes ← ۵۱۲ بایت (تا ۸ عکس × ۳ نسخه) و ستون تازهٔ Palettes (رنگ هر عکس) برای جست‌وجوی تصویری دقیق.
    /// امضاهای قبلی Palettes ندارند و خودکار دوباره ساخته می‌شوند.</item>
    /// </list>
    /// </summary>
    public partial class Round30 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CategoryImageFeedbacks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImageName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UrlKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Hash = table.Column<long>(type: "bigint", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Query = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Features = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Decision = table.Column<byte>(type: "tinyint", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryImageFeedbacks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CategoryImageFeedbacks_CategoryId_ImageName",
                table: "CategoryImageFeedbacks",
                columns: new[] { "CategoryId", "ImageName" });

            migrationBuilder.CreateIndex(
                name: "IX_CategoryImageFeedbacks_Decision",
                table: "CategoryImageFeedbacks",
                column: "Decision");

            migrationBuilder.AlterColumn<byte[]>(
                name: "Hashes",
                table: "ProductImageSignatures",
                type: "varbinary(512)",
                maxLength: 512,
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "varbinary(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "Palettes",
                table: "ProductImageSignatures",
                type: "varbinary(1024)",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CategoryImageFeedbacks");
            migrationBuilder.DropColumn(name: "Palettes", table: "ProductImageSignatures");
            // امضاهای بزرگ‌تر از ستون قدیمی جا نمی‌شوند؛ خالی می‌شوند تا دوباره ساخته شوند.
            migrationBuilder.Sql("UPDATE [ProductImageSignatures] SET [Hashes] = NULL WHERE DATALENGTH([Hashes]) > 128;");
            migrationBuilder.AlterColumn<byte[]>(
                name: "Hashes",
                table: "ProductImageSignatures",
                type: "varbinary(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "varbinary(512)",
                oldMaxLength: 512,
                oldNullable: true);
        }
    }
}
