using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <summary>
    /// دور سئوی خودکار (دست‌نویس، چون CI امکان ساخت خودکار مهاجرت را ندارد): نُه جدول تازه.
    /// <para>SeoAutopilotConfigs (تنظیمات)، SeoAutoStates (وضعیت هر صفحه)، SeoRevisions (تاریخچه و برگرداندن)،
    /// SeoCrawlRuns/SeoCrawlPages (خزش داخلی)، SeoTasks (کارهای سئو)، SeoBotVisits (بازدید ربات‌ها)،
    /// SeoVitalStats (سرعت واقعی کاربران) و SeoTitleTests (آزمایش عنوان).</para>
    /// <para>هیچ ستون جدول‌های قبلی تغییر نمی‌کند؛ تبدیل نامک‌ها و تصویرها کار پس‌زمینهٔ برنامه است، نه این مهاجرت.</para>
    /// </summary>
    public partial class SeoAutopilot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SeoAutopilotConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OptionsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IndexNowKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    BingApiKeyProtected = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastNightlyRunAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastNightlySummary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    LastWeeklyReportAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SlugMigrationDoneAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ImageMigrationDoneAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    BacklinksJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BacklinksFetchedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SslStatus = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SslExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeoAutopilotConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeoAutoStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StampsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastFilledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastHtmlFixAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastRewriteAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastHtmlHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeoAutoStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeoRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Path = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    PageTitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Source = table.Column<byte>(type: "tinyint", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    RevertedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevertedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeoRevisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeoCrawlRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Pages = table.Column<int>(type: "int", nullable: false),
                    Errors = table.Column<int>(type: "int", nullable: false),
                    Warnings = table.Column<int>(type: "int", nullable: false),
                    HealthScore = table.Column<int>(type: "int", nullable: false),
                    SummaryJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeoCrawlRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeoCrawlPages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Path = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Depth = table.Column<int>(type: "int", nullable: false),
                    Ms = table.Column<int>(type: "int", nullable: false),
                    Bytes = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    H1 = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    H1Count = table.Column<int>(type: "int", nullable: false),
                    Words = table.Column<int>(type: "int", nullable: false),
                    Images = table.Column<int>(type: "int", nullable: false),
                    ImagesWithoutAlt = table.Column<int>(type: "int", nullable: false),
                    InLinks = table.Column<int>(type: "int", nullable: false),
                    OutLinks = table.Column<int>(type: "int", nullable: false),
                    ExternalLinks = table.Column<int>(type: "int", nullable: false),
                    Canonical = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    NoIndex = table.Column<bool>(type: "bit", nullable: false),
                    InSitemap = table.Column<bool>(type: "bit", nullable: false),
                    SchemaTypes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SchemaErrors = table.Column<int>(type: "int", nullable: false),
                    SimHash = table.Column<long>(type: "bigint", nullable: false),
                    IssuesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeoCrawlPages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeoTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Severity = table.Column<byte>(type: "tinyint", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Path = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Impact = table.Column<int>(type: "int", nullable: false),
                    AutoFixable = table.Column<bool>(type: "bit", nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResolvedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeoTasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeoBotVisits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Bot = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Path = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Hits = table.Column<int>(type: "int", nullable: false),
                    VerifiedHits = table.Column<int>(type: "int", nullable: false),
                    FakeHits = table.Column<int>(type: "int", nullable: false),
                    TotalMs = table.Column<long>(type: "bigint", nullable: false),
                    LastAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeoBotVisits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeoVitalStats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    PageType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Device = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Metric = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    P75 = table.Column<double>(type: "float", nullable: false),
                    Samples = table.Column<int>(type: "int", nullable: false),
                    Good = table.Column<int>(type: "int", nullable: false),
                    Poor = table.Column<int>(type: "int", nullable: false),
                    AppVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeoVitalStats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeoTitleTests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Path = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OldTitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    NewTitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    OldMeta = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    NewMeta = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EvaluatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    BaselineCtr = table.Column<double>(type: "float", nullable: false),
                    BaselinePosition = table.Column<double>(type: "float", nullable: false),
                    BaselineImpressions = table.Column<int>(type: "int", nullable: false),
                    ResultCtr = table.Column<double>(type: "float", nullable: true),
                    ResultPosition = table.Column<double>(type: "float", nullable: true),
                    ResultImpressions = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeoTitleTests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SeoAutoStates_Kind_EntityId",
                table: "SeoAutoStates",
                columns: new[] { "Kind", "EntityId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SeoRevisions_CreatedAt",
                table: "SeoRevisions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SeoRevisions_Kind_EntityId",
                table: "SeoRevisions",
                columns: new[] { "Kind", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_SeoCrawlRuns_StartedAt",
                table: "SeoCrawlRuns",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SeoCrawlPages_RunId_Path",
                table: "SeoCrawlPages",
                columns: new[] { "RunId", "Path" });

            migrationBuilder.CreateIndex(
                name: "IX_SeoTasks_Fingerprint",
                table: "SeoTasks",
                column: "Fingerprint",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SeoTasks_Status_Impact",
                table: "SeoTasks",
                columns: new[] { "Status", "Impact" });

            migrationBuilder.CreateIndex(
                name: "IX_SeoBotVisits_Date_Bot_Path_Status",
                table: "SeoBotVisits",
                columns: new[] { "Date", "Bot", "Path", "Status" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SeoVitalStats_Date_PageType_Device_Metric",
                table: "SeoVitalStats",
                columns: new[] { "Date", "PageType", "Device", "Metric" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SeoTitleTests_Status",
                table: "SeoTitleTests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SeoTitleTests_Kind_EntityId",
                table: "SeoTitleTests",
                columns: new[] { "Kind", "EntityId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SeoAutopilotConfigs");

            migrationBuilder.DropTable(
                name: "SeoAutoStates");

            migrationBuilder.DropTable(
                name: "SeoRevisions");

            migrationBuilder.DropTable(
                name: "SeoCrawlRuns");

            migrationBuilder.DropTable(
                name: "SeoCrawlPages");

            migrationBuilder.DropTable(
                name: "SeoTasks");

            migrationBuilder.DropTable(
                name: "SeoBotVisits");

            migrationBuilder.DropTable(
                name: "SeoVitalStats");

            migrationBuilder.DropTable(
                name: "SeoTitleTests");
        }
    }
}
