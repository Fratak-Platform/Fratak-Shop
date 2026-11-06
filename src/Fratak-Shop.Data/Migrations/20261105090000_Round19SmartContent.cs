using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <summary>
    /// دور نوزدهم (دست‌نویس، چون CI امکان ساخت خودکار مهاجرت را ندارد): شش جدول تازهٔ «محتوای هوشمند» و «فروش هوشمند».
    /// <para>SmartTasks (صف کارهای هوشمند)، SmartResearchCaches (کش ۳۰ روزهٔ تحقیق اینترنتی)، ProductInsights (جمع‌بندی نظرها و نکات کلیدی)،
    /// BlogPlanItems (تقویم محتوا)، CustomerReminders (درخواست نظر و یادآوری تمدید) و SmartContentConfigs (تنظیمات + کلید هش‌شدهٔ دستیار).</para>
    /// <para>هیچ ستون جدول‌های قبلی تغییر نمی‌کند و هیچ کلید خارجی ساخته نمی‌شود (حذف محصول/مقاله هرگز گیر نمی‌کند).</para>
    /// </summary>
    public partial class Round19SmartContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BlogPlanItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ArticleType = table.Column<byte>(type: "tinyint", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Keyword = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Origin = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PlannedFor = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PostId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProductIds = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    QualityScore = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Topic = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlogPlanItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerReminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerReminders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductInsights",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ConsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FaqJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ForWhomJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    HighlightsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NotForJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RatingTenths = table.Column<int>(type: "int", nullable: true),
                    ReviewCount = table.Column<int>(type: "int", nullable: false),
                    ShortDescription = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SourcesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(1500)", maxLength: 1500, nullable: true),
                    Verdict = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductInsights", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SmartContentConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HelperKeyHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    HelperKeyHint = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    HelperLastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    HelperName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastBriefAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastPlanAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    OptionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartContentConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SmartResearchCaches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartResearchCaches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SmartTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    ClaimedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    RequestedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RunAfter = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TargetKind = table.Column<byte>(type: "tinyint", nullable: false),
                    TargetTitle = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartTasks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlogPlanItems_Status_PlannedFor",
                table: "BlogPlanItems",
                columns: new[] { "Status", "PlannedFor" });
            migrationBuilder.CreateIndex(
                name: "IX_CustomerReminders_Kind_OrderId_ProductId",
                table: "CustomerReminders",
                columns: new[] { "Kind", "OrderId", "ProductId" },
                unique: true,
                filter: "[IsDeleted] = 0");
            migrationBuilder.CreateIndex(
                name: "IX_CustomerReminders_Status_DueAt",
                table: "CustomerReminders",
                columns: new[] { "Status", "DueAt" });
            migrationBuilder.CreateIndex(
                name: "IX_ProductInsights_ProductId",
                table: "ProductInsights",
                column: "ProductId",
                unique: true,
                filter: "[IsDeleted] = 0");
            migrationBuilder.CreateIndex(
                name: "IX_SmartResearchCaches_ExpiresAt",
                table: "SmartResearchCaches",
                column: "ExpiresAt");
            migrationBuilder.CreateIndex(
                name: "IX_SmartResearchCaches_Key",
                table: "SmartResearchCaches",
                column: "Key",
                unique: true,
                filter: "[IsDeleted] = 0");
            migrationBuilder.CreateIndex(
                name: "IX_SmartTasks_Status_RunAfter",
                table: "SmartTasks",
                columns: new[] { "Status", "RunAfter" });
            migrationBuilder.CreateIndex(
                name: "IX_SmartTasks_TargetKind_TargetId",
                table: "SmartTasks",
                columns: new[] { "TargetKind", "TargetId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlogPlanItems");

            migrationBuilder.DropTable(
                name: "CustomerReminders");

            migrationBuilder.DropTable(
                name: "ProductInsights");

            migrationBuilder.DropTable(
                name: "SmartContentConfigs");

            migrationBuilder.DropTable(
                name: "SmartResearchCaches");

            migrationBuilder.DropTable(
                name: "SmartTasks");
        }
    }
}
