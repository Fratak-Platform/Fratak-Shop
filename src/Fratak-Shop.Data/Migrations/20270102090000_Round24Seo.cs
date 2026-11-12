using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <summary>
    /// دور ۲۴ بستهٔ E (دست‌نویس): ستون TrustBadges.EmbedCode (پیشنهاد ۴۶) – کد نماد چسبانده‌شده (مثلاً اینماد) برای نمایش
    /// دوباره در پنل؛ هرگز خام در سایت چاپ نمی‌شود. Down ستون را برمی‌دارد.
    /// </summary>
    public partial class Round24Seo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmbedCode",
                table: "TrustBadges",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmbedCode",
                table: "TrustBadges");
        }
    }
}
