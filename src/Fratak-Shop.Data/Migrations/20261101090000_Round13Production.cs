using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <summary>
    /// مرحلهٔ سیزدهم (آمادگی انتشار روی faratek.ir؛ دست‌نویس، چون CI امکان ساخت خودکار مهاجرت را نداشت):
    /// کش ماندگار قیمت رقبا (CompetitorPriceCaches) و صف قیمت‌گذاری بازار محصولات درون‌ریزی‌شده (MarketPriceChecks).
    /// </summary>
    public partial class Round13Production : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompetitorPriceCaches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QueryKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OffersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MatchedTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OfferCount = table.Column<int>(type: "int", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitorPriceCaches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MarketPriceChecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Query = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    InitialPrice = table.Column<decimal>(type: "decimal(18,0)", precision: 18, nullable: false),
                    PurchasePrice = table.Column<decimal>(type: "decimal(18,0)", precision: 18, nullable: true),
                    LowestPrice = table.Column<decimal>(type: "decimal(18,0)", precision: 18, nullable: true),
                    MedianPrice = table.Column<decimal>(type: "decimal(18,0)", precision: 18, nullable: true),
                    OfferCount = table.Column<int>(type: "int", nullable: false),
                    SuggestedPrice = table.Column<decimal>(type: "decimal(18,0)", precision: 18, nullable: true),
                    MatchedTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketPriceChecks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompetitorPriceCaches_FetchedAt",
                table: "CompetitorPriceCaches",
                column: "FetchedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitorPriceCaches_QueryKey",
                table: "CompetitorPriceCaches",
                column: "QueryKey",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MarketPriceChecks_ProductId",
                table: "MarketPriceChecks",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketPriceChecks_Status_NextAttemptAt",
                table: "MarketPriceChecks",
                columns: new[] { "Status", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompetitorPriceCaches");

            migrationBuilder.DropTable(
                name: "MarketPriceChecks");
        }
    }
}
