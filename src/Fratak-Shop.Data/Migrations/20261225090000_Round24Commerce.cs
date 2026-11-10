using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <summary>
    /// دور ۲۴ بستهٔ B (دست‌نویس): سه جدول تازه.
    /// SearchClickStats (رتبه‌بندی یادگیرندهٔ جست‌وجو)، ProductImageSignatures (جست‌وجوی تصویری) و PriceMatchRequests (تضمین قیمت).
    /// Down هر سه جدول را برمی‌دارد.
    /// </summary>
    public partial class Round24Commerce : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PriceMatchRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CompetitorUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CompetitorPrice = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    OurPrice = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    AdminNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CouponCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewerName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceMatchRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductImageSignatures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImageName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Hash = table.Column<long>(type: "bigint", nullable: false),
                    Colors = table.Column<byte[]>(type: "varbinary(32)", maxLength: 32, nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductImageSignatures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SearchClickStats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NormalizedQuery = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Clicks = table.Column<int>(type: "int", nullable: false),
                    LastClickAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchClickStats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PriceMatchRequests_Status_CreatedAt",
                table: "PriceMatchRequests",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PriceMatchRequests_UserId_CreatedAt",
                table: "PriceMatchRequests",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductImageSignatures_ProductId",
                table: "ProductImageSignatures",
                column: "ProductId",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SearchClickStats_LastClickAt",
                table: "SearchClickStats",
                column: "LastClickAt");

            migrationBuilder.CreateIndex(
                name: "IX_SearchClickStats_NormalizedQuery_ProductId",
                table: "SearchClickStats",
                columns: new[] { "NormalizedQuery", "ProductId" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PriceMatchRequests");

            migrationBuilder.DropTable(
                name: "ProductImageSignatures");

            migrationBuilder.DropTable(
                name: "SearchClickStats");
        }
    }
}
