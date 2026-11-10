using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <summary>
    /// دور ۲۴ بستهٔ C (دست‌نویس): بنر هوشمند.
    /// جدول CampaignOffers (قاعدهٔ واقعی تخفیف/امتیاز پشت بنر کمپین که با انتشار بنر فعال می‌شود)
    /// و ستون DesignDocs.CategoryId (دستهٔ ایده برای بنر «سربرگ صفحهٔ دسته»).
    /// Down هر دو را برمی‌دارد.
    /// </summary>
    public partial class Round24Campaign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "DesignDocs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CampaignOffers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    VariantGroup = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Scope = table.Column<byte>(type: "tinyint", nullable: false),
                    ScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ScopeName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DiscountPercent = table.Column<int>(type: "int", nullable: true),
                    PointsMultiplier = table.Column<decimal>(type: "decimal(4,2)", precision: 4, scale: 2, nullable: true),
                    CouponId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CouponCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DurationDays = table.Column<int>(type: "int", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    CreatedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ActivatedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignOffers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignOffers_Status_EndsAt",
                table: "CampaignOffers",
                columns: new[] { "Status", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignOffers_VariantGroup",
                table: "CampaignOffers",
                column: "VariantGroup");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampaignOffers");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "DesignDocs");
        }
    }
}
