using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <summary>
    /// مرحلهٔ پانزدهم (دست‌نویس، چون CI امکان ساخت خودکار مهاجرت را ندارد):
    /// ۱) کلید ثابت «مدیر اصلی سیستم» روی کاربران (Users.SystemAdminKey)؛
    /// ۲) جدول آخرین قیمت رقبای هر محصول (ProductCompetitorSnapshots).
    /// <para>با این کلید، مدیران تعریف‌شده در Shop:DefaultAdmins در هر اجرا پیدا می‌شوند حتی اگر شماره، نام یا رمزشان را
    /// عوض کرده باشند؛ و اگر حسابشان حذف یا مسدود شده باشد برگردانده می‌شود. ستون nullable است؛ کاربران فعلی دست نمی‌خورند.</para>
    /// </summary>
    public partial class Round15AdminsCompetitors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SystemAdminKey",
                table: "Users",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_SystemAdminKey",
                table: "Users",
                column: "SystemAdminKey",
                unique: true,
                filter: "[SystemAdminKey] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateTable(
                name: "ProductCompetitorSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Query = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MatchedTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OffersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OfferCount = table.Column<int>(type: "int", nullable: false),
                    AcceptedCount = table.Column<int>(type: "int", nullable: false),
                    LowestPrice = table.Column<decimal>(type: "decimal(18,0)", precision: 18, nullable: true),
                    MedianPrice = table.Column<decimal>(type: "decimal(18,0)", precision: 18, nullable: true),
                    TorobLowestPrice = table.Column<decimal>(type: "decimal(18,0)", precision: 18, nullable: true),
                    TorobSellerCount = table.Column<int>(type: "int", nullable: false),
                    OurPrice = table.Column<decimal>(type: "decimal(18,0)", precision: 18, nullable: true),
                    Problems = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductCompetitorSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductCompetitorSnapshots_CheckedAt",
                table: "ProductCompetitorSnapshots",
                column: "CheckedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ProductCompetitorSnapshots_ProductId",
                table: "ProductCompetitorSnapshots",
                column: "ProductId",
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductCompetitorSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_Users_SystemAdminKey",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SystemAdminKey",
                table: "Users");
        }
    }
}
