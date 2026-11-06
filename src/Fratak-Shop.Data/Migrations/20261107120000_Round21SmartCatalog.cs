using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <summary>
    /// دور ۲۱ (دست‌نویس): ظاهر کامل نوار اطلاعیهٔ بالای سایت + نشانه‌های دسته‌بندی هوشمند.
    /// <list type="bullet">
    /// <item>StoreSettings: سبک، جلوه، اندازه/وزن قلم، رنگ دوم، رنگ متن، پیام‌های چرخشی و «بستنی بودن» نوار اطلاعیه (پیش‌فرض‌ها = ظاهر قبلی).</item>
    /// <item>Categories: IsSystem (دستهٔ پنهان «دسته‌بندی نشده») و IsAutoCreated (ساخت دسته‌بندی هوشمند).</item>
    /// </list>
    /// هیچ دادهٔ موجودی تغییر نمی‌کند؛ Down ستون‌ها را حذف می‌کند.
    /// </summary>
    public partial class Round21SmartCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "AnnouncementColor2", table: "StoreSettings", type: "nvarchar(20)", maxLength: 20, nullable: true);
            migrationBuilder.AddColumn<string>(name: "AnnouncementTextColor", table: "StoreSettings", type: "nvarchar(20)", maxLength: 20, nullable: true);
            migrationBuilder.AddColumn<int>(name: "AnnouncementStyle", table: "StoreSettings", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "AnnouncementEffect", table: "StoreSettings", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "AnnouncementFontSize", table: "StoreSettings", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "AnnouncementFontWeight", table: "StoreSettings", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<string>(name: "AnnouncementMessages", table: "StoreSettings", type: "nvarchar(1000)", maxLength: 1000, nullable: true);
            migrationBuilder.AddColumn<bool>(name: "IsAnnouncementCloseable", table: "StoreSettings", type: "bit", nullable: false, defaultValue: false);

            migrationBuilder.AddColumn<bool>(name: "IsSystem", table: "Categories", type: "bit", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<bool>(name: "IsAutoCreated", table: "Categories", type: "bit", nullable: false, defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AnnouncementColor2", table: "StoreSettings");
            migrationBuilder.DropColumn(name: "AnnouncementTextColor", table: "StoreSettings");
            migrationBuilder.DropColumn(name: "AnnouncementStyle", table: "StoreSettings");
            migrationBuilder.DropColumn(name: "AnnouncementEffect", table: "StoreSettings");
            migrationBuilder.DropColumn(name: "AnnouncementFontSize", table: "StoreSettings");
            migrationBuilder.DropColumn(name: "AnnouncementFontWeight", table: "StoreSettings");
            migrationBuilder.DropColumn(name: "AnnouncementMessages", table: "StoreSettings");
            migrationBuilder.DropColumn(name: "IsAnnouncementCloseable", table: "StoreSettings");
            migrationBuilder.DropColumn(name: "IsSystem", table: "Categories");
            migrationBuilder.DropColumn(name: "IsAutoCreated", table: "Categories");
        }
    }
}
