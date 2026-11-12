using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round7Seo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSeoEngineEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "SeoAllowSlugChanges",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "SeoCompetitorResearch",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "SeoEngineBatchSize",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "SeoEngineRunsPerDay",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<string>(
                name: "Seo_FaqJson",
                table: "Products",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Seo_FaqJson",
                table: "PageSeos",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Seo_FaqJson",
                table: "ContentPages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Seo_FaqJson",
                table: "Categories",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Seo_FaqJson",
                table: "Brands",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Seo_FaqJson",
                table: "BlogPosts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Seo_FaqJson",
                table: "BlogCategories",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SeoEngineRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SlotKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Trigger = table.Column<byte>(type: "tinyint", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Analyzed = table.Column<int>(type: "int", nullable: false),
                    Suggested = table.Column<int>(type: "int", nullable: false),
                    Failed = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeoEngineRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeoResearchCaches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Ok = table.Column<bool>(type: "bit", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeoResearchCaches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeoScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Path = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    Problems = table.Column<int>(type: "int", nullable: false),
                    FocusKeyword = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ChecksJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AnalyzedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Views = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeoScores", x => x.Id);
                    table.CheckConstraint("CK_SeoScores_Score", "[Score] BETWEEN 0 AND 100");
                });

            migrationBuilder.CreateTable(
                name: "SeoSuggestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Path = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Fields = table.Column<int>(type: "int", nullable: false),
                    ScoreBefore = table.Column<int>(type: "int", nullable: false),
                    ScoreAfter = table.Column<int>(type: "int", nullable: false),
                    Variant = table.Column<int>(type: "int", nullable: false),
                    ProposalJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CurrentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReasonsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResearchJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RejectReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RejectedFields = table.Column<int>(type: "int", nullable: false),
                    AppliedFields = table.Column<int>(type: "int", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeoSuggestions", x => x.Id);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_StoreSettings_SeoEngine",
                table: "StoreSettings",
                sql: "[SeoEngineRunsPerDay] BETWEEN 1 AND 6 AND [SeoEngineBatchSize] BETWEEN 5 AND 200");

            migrationBuilder.CreateIndex(
                name: "IX_SeoEngineRuns_SlotKey",
                table: "SeoEngineRuns",
                column: "SlotKey",
                unique: true,
                filter: "[SlotKey] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SeoEngineRuns_StartedAt",
                table: "SeoEngineRuns",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SeoResearchCaches_ExpiresAt",
                table: "SeoResearchCaches",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_SeoResearchCaches_Kind_Key",
                table: "SeoResearchCaches",
                columns: new[] { "Kind", "Key" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SeoScores_Kind_EntityId",
                table: "SeoScores",
                columns: new[] { "Kind", "EntityId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SeoScores_Score_Views",
                table: "SeoScores",
                columns: new[] { "Score", "Views" });

            migrationBuilder.CreateIndex(
                name: "IX_SeoSuggestions_Kind_EntityId_Status",
                table: "SeoSuggestions",
                columns: new[] { "Kind", "EntityId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SeoSuggestions_Status_CreatedAt",
                table: "SeoSuggestions",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SeoEngineRuns");

            migrationBuilder.DropTable(
                name: "SeoResearchCaches");

            migrationBuilder.DropTable(
                name: "SeoScores");

            migrationBuilder.DropTable(
                name: "SeoSuggestions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StoreSettings_SeoEngine",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsSeoEngineEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "SeoAllowSlugChanges",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "SeoCompetitorResearch",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "SeoEngineBatchSize",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "SeoEngineRunsPerDay",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "Seo_FaqJson",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Seo_FaqJson",
                table: "PageSeos");

            migrationBuilder.DropColumn(
                name: "Seo_FaqJson",
                table: "ContentPages");

            migrationBuilder.DropColumn(
                name: "Seo_FaqJson",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "Seo_FaqJson",
                table: "Brands");

            migrationBuilder.DropColumn(
                name: "Seo_FaqJson",
                table: "BlogPosts");

            migrationBuilder.DropColumn(
                name: "Seo_FaqJson",
                table: "BlogCategories");
        }
    }
}
