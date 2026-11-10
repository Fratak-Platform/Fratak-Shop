using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <summary>
    /// دور ۲۳ (دست‌نویس): درخت دسته‌ها، بنرهای هوشمند، اثر انگشت تصویر و دستیار فروشگاه.
    /// <list type="bullet">
    /// <item>Categories.IconKey: نام آیکون دسته (خالی ← ممیز درخت خودش انتخاب می‌کند).</item>
    /// <item>Banners.ProductId / CategoryId / VariantGroup / Views / Clicks: پنهان شدن خودکار بنر کالای ناموجود، سربرگ دسته و آزمون A/B.</item>
    /// <item>ProductImages.Hash: dHash ۶۴ بیتی برای رد عکس تکراری.</item>
    /// <item>جدول AssistantMessages: پرسش‌های مشتری از دستیار (برای پنل «پرسش‌های پرتکرار، کالاهای درخواستی، کمبود اطلاعات»).</item>
    /// </list>
    /// Down همه را برمی‌گرداند.
    /// </summary>
    public partial class Round23Tree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "IconKey", table: "Categories", type: "nvarchar(40)", maxLength: 40, nullable: true);

            migrationBuilder.AddColumn<Guid>(name: "ProductId", table: "Banners", type: "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "CategoryId", table: "Banners", type: "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<string>(name: "VariantGroup", table: "Banners", type: "nvarchar(60)", maxLength: 60, nullable: true);
            migrationBuilder.AddColumn<int>(name: "Views", table: "Banners", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "Clicks", table: "Banners", type: "int", nullable: false, defaultValue: 0);

            migrationBuilder.AddColumn<long>(name: "Hash", table: "ProductImages", type: "bigint", nullable: true);

            migrationBuilder.CreateTable(
                name: "AssistantMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Text = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Intent = table.Column<byte>(type: "tinyint", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Answered = table.Column<bool>(type: "bit", nullable: false),
                    Topic = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Query = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                },
                constraints: table => table.PrimaryKey("PK_AssistantMessages", x => x.Id));

            migrationBuilder.CreateIndex(name: "IX_AssistantMessages_CreatedAt", table: "AssistantMessages", column: "CreatedAt");
            migrationBuilder.CreateIndex(name: "IX_AssistantMessages_ProductId", table: "AssistantMessages", column: "ProductId");
            migrationBuilder.CreateIndex(name: "IX_AssistantMessages_Intent_CreatedAt", table: "AssistantMessages", columns: new[] { "Intent", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AssistantMessages");
            migrationBuilder.DropColumn(name: "Hash", table: "ProductImages");
            migrationBuilder.DropColumn(name: "Clicks", table: "Banners");
            migrationBuilder.DropColumn(name: "Views", table: "Banners");
            migrationBuilder.DropColumn(name: "VariantGroup", table: "Banners");
            migrationBuilder.DropColumn(name: "CategoryId", table: "Banners");
            migrationBuilder.DropColumn(name: "ProductId", table: "Banners");
            migrationBuilder.DropColumn(name: "IconKey", table: "Categories");
        }
    }
}
