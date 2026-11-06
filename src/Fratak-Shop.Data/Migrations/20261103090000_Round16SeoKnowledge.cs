using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <summary>
    /// مرحلهٔ شانزدهم (دست‌نویس، چون CI امکان ساخت خودکار مهاجرت را ندارد):
    /// ۱) جدول دانش سئو و نگارش (SeoKnowledgeEntries) — «هوش» رایگان موتور سئو و دکمهٔ «پیشنهاد هوشمند»؛
    /// ۲) بازخورد پیشنهادهای هوشمند متن (SmartTextFeedbacks) — کدام نسخه بیشتر پذیرفته می‌شود.
    /// <para>داده‌های اولیه (حدود ۳۳ هزار ردیف) این‌جا درج نمی‌شود؛ سرویس پس‌زمینهٔ SeoKnowledgeSeedWorker چند ثانیه بعد از
    /// راه‌اندازی با SqlBulkCopy می‌ریزد تا مهاجرت سبک بماند و راه‌اندازی کند نشود.</para>
    /// </summary>
    public partial class Round16SeoKnowledge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SeoKnowledgeEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    Term = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Value = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Topic = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, defaultValue: "", collation: "Latin1_General_100_BIN2"),
                    Weight = table.Column<double>(type: "float", nullable: false),
                    Uses = table.Column<int>(type: "int", nullable: false),
                    Successes = table.Column<int>(type: "int", nullable: false),
                    Rejections = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<byte>(type: "tinyint", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeoKnowledgeEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SmartTextFeedbacks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Variant = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Accepted = table.Column<bool>(type: "bit", nullable: false),
                    Rules = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    InputHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    InputLength = table.Column<int>(type: "int", nullable: false),
                    OutputLength = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartTextFeedbacks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SeoKnowledgeEntries_Kind_Source",
                table: "SeoKnowledgeEntries",
                columns: new[] { "Kind", "Source" });

            migrationBuilder.CreateIndex(
                name: "IX_SeoKnowledgeEntries_Kind_Term_Topic",
                table: "SeoKnowledgeEntries",
                columns: new[] { "Kind", "Term", "Topic" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SmartTextFeedbacks_Purpose_Variant",
                table: "SmartTextFeedbacks",
                columns: new[] { "Purpose", "Variant" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SeoKnowledgeEntries");

            migrationBuilder.DropTable(
                name: "SmartTextFeedbacks");
        }
    }
}
