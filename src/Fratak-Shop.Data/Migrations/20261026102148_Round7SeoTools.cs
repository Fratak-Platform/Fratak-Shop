using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round7SeoTools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SearchConsoleDailyStats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Clicks = table.Column<int>(type: "int", nullable: false),
                    Impressions = table.Column<int>(type: "int", nullable: false),
                    Ctr = table.Column<double>(type: "float", nullable: false),
                    Position = table.Column<double>(type: "float", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchConsoleDailyStats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SearchConsoleQueryStats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Path = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Query = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Clicks = table.Column<int>(type: "int", nullable: false),
                    Impressions = table.Column<int>(type: "int", nullable: false),
                    Ctr = table.Column<double>(type: "float", nullable: false),
                    Position = table.Column<double>(type: "float", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchConsoleQueryStats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeoLinkIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Path = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    UrlHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AnchorText = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsImage = table.Column<bool>(type: "bit", nullable: false),
                    IsInternal = table.Column<bool>(type: "bit", nullable: false),
                    Problem = table.Column<byte>(type: "tinyint", nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: true),
                    SuggestedUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Occurrences = table.Column<int>(type: "int", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsIgnored = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeoLinkIssues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeoToolsConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsSearchConsoleEnabled = table.Column<bool>(type: "bit", nullable: false),
                    SearchConsoleSite = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SearchConsoleKeyProtected = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ServiceAccountEmail = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SearchConsoleProxy = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SearchConsoleSyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SearchConsoleOk = table.Column<bool>(type: "bit", nullable: false),
                    SearchConsoleStatus = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsLinkCheckEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CheckExternalLinks = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    LinkCheckAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LinkCheckSummary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeoToolsConfigs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SearchConsoleDailyStats_Date",
                table: "SearchConsoleDailyStats",
                column: "Date",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SearchConsoleQueryStats_Impressions",
                table: "SearchConsoleQueryStats",
                column: "Impressions");

            migrationBuilder.CreateIndex(
                name: "IX_SearchConsoleQueryStats_Path",
                table: "SearchConsoleQueryStats",
                column: "Path");

            migrationBuilder.CreateIndex(
                name: "IX_SeoLinkIssues_Kind_EntityId_UrlHash",
                table: "SeoLinkIssues",
                columns: new[] { "Kind", "EntityId", "UrlHash" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SeoLinkIssues_Problem_IsIgnored",
                table: "SeoLinkIssues",
                columns: new[] { "Problem", "IsIgnored" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SearchConsoleDailyStats");

            migrationBuilder.DropTable(
                name: "SearchConsoleQueryStats");

            migrationBuilder.DropTable(
                name: "SeoLinkIssues");

            migrationBuilder.DropTable(
                name: "SeoToolsConfigs");
        }
    }
}
