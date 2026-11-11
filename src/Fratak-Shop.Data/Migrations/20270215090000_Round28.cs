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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SubjectScore", table: "ProductImages");
            migrationBuilder.DropColumn(name: "SubjectFlags", table: "ProductImages");
            migrationBuilder.DropColumn(name: "SeenPrice", table: "CartItems");
        }
    }
}
