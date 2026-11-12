using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <summary>
    /// دور ۲۸ (دست‌نویس): بررسی سوژهٔ عکس‌ها و قیمت دیده‌شدهٔ سبد.
    /// <list type="bullet">
    /// <item>ProductImages.SubjectScore – اطمینان «عکس خود کالاست» ۰ تا ۱۰۰ (null = هنوز بررسی نشده).</item>
    /// <item>ProductImages.SubjectFlags – نشانه‌ها: ۱ سفید، ۲ یکدست، ۴ آدم، ۸ واترمارک، ۱۶ صحنه.</item>
    /// <item>CartItems.SeenPrice – قیمتی که مشتری هنگام افزودن به سبد دید (برای هشدار تغییر قیمت در تسویه).</item>
    /// <item>جدول AssistantFacts – «دانش دستیار» (واژه‌نامه، راهنمای خرید، گپ کوتاه، هم‌معنی‌ها، یادگرفته‌ها) با شمارنده‌های یادگیری.</item>
    /// </list>
    /// همه nullable و بدون مقدار پیش‌فرض‌اند؛ Down همه را برمی‌دارد.
    /// </summary>
    public partial class Round28 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "SubjectScore",
                table: "ProductImages",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "SubjectFlags",
                table: "ProductImages",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SeenPrice",
                table: "CartItems",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssistantFacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    Term = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Aliases = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    Answer = table.Column<string>(type: "nvarchar(1500)", maxLength: 1500, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Topic = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Weight = table.Column<double>(type: "float", nullable: false),
                    Shown = table.Column<int>(type: "int", nullable: false),
                    Helpful = table.Column<int>(type: "int", nullable: false),
                    Unhelpful = table.Column<int>(type: "int", nullable: false),
                    IsSeed = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantFacts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantFacts_IsActive",
                table: "AssistantFacts",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantFacts_Kind_Term",
                table: "AssistantFacts",
                columns: new[] { "Kind", "Term" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AssistantFacts");
            migrationBuilder.DropColumn(name: "SubjectScore", table: "ProductImages");
            migrationBuilder.DropColumn(name: "SubjectFlags", table: "ProductImages");
            migrationBuilder.DropColumn(name: "SeenPrice", table: "CartItems");
        }
    }
}
