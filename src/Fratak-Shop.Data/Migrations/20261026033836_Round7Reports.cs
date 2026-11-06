using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round7Reports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    SpentOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Vendor = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Recurrence = table.Column<int>(type: "int", nullable: false),
                    NextDueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    RecurringParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastReminderOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                    table.CheckConstraint("CK_Expenses_Amount", "[Amount] > 0");
                });

            migrationBuilder.CreateTable(
                name: "ReportDeliveryConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SmtpHost = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SmtpPort = table.Column<int>(type: "int", nullable: false),
                    SmtpUserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SmtpPasswordProtected = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FromAddress = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FromName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BaleTokenProtected = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TelegramTokenProtected = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportDeliveryConfigs", x => x.Id);
                    table.CheckConstraint("CK_ReportDeliveryConfigs_Port", "[SmtpPort] BETWEEN 1 AND 65535");
                });

            migrationBuilder.CreateTable(
                name: "SalesGoals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    TargetAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    TargetOrders = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SetByName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesGoals", x => x.Id);
                    table.CheckConstraint("CK_SalesGoals_Month", "[Month] BETWEEN 1 AND 12");
                    table.CheckConstraint("CK_SalesGoals_Target", "[TargetAmount] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "ScheduledReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Frequency = table.Column<int>(type: "int", nullable: false),
                    Hour = table.Column<byte>(type: "tinyint", nullable: false),
                    WeekDay = table.Column<byte>(type: "tinyint", nullable: false),
                    Channels = table.Column<int>(type: "int", nullable: false),
                    Sections = table.Column<int>(type: "int", nullable: false),
                    EmailTo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BaleChatId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TelegramChatId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    NextRunAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastRunAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastSucceeded = table.Column<bool>(type: "bit", nullable: true),
                    LastStatus = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledReports", x => x.Id);
                    table.CheckConstraint("CK_ScheduledReports_Hour", "[Hour] BETWEEN 0 AND 23");
                    table.CheckConstraint("CK_ScheduledReports_WeekDay", "[WeekDay] BETWEEN 0 AND 6");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_NextDueOn",
                table: "Expenses",
                column: "NextDueOn",
                filter: "[NextDueOn] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_RecurringParentId",
                table: "Expenses",
                column: "RecurringParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_SpentOn_Category",
                table: "Expenses",
                columns: new[] { "SpentOn", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesGoals_Year_Month",
                table: "SalesGoals",
                columns: new[] { "Year", "Month" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledReports_IsActive_NextRunAt",
                table: "ScheduledReports",
                columns: new[] { "IsActive", "NextRunAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Expenses");

            migrationBuilder.DropTable(
                name: "ReportDeliveryConfigs");

            migrationBuilder.DropTable(
                name: "SalesGoals");

            migrationBuilder.DropTable(
                name: "ScheduledReports");
        }
    }
}
