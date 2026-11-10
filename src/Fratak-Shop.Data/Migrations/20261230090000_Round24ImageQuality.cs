using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <summary>
    /// دور ۲۴ بستهٔ D (دست‌نویس): ستون ProductImages.Quality (امتیاز کیفیت ۰ تا ۱۰۰ برای انتخاب بهترین عکس اصلی کالا).
    /// null = هنوز محاسبه نشده. Down ستون را برمی‌دارد.
    /// </summary>
    public partial class Round24ImageQuality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "Quality",
                table: "ProductImages",
                type: "tinyint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Quality",
                table: "ProductImages");
        }
    }
}
