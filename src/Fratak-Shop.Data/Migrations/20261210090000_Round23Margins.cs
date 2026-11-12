using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <summary>
    /// دور ۲۳ (دست‌نویس، مورد ۶۹): کف سود هر دسته. ستون Categories.MinMarginPercent (خالی = کف سراسری تنظیمات).
    /// Down ستون را برمی‌دارد.
    /// </summary>
    public partial class Round23Margins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(name: "MinMarginPercent", table: "Categories", type: "int", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "MinMarginPercent", table: "Categories");
        }
    }
}
