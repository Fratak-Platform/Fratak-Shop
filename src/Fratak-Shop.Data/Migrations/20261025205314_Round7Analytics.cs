using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round7Analytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AnalyticsRetentionDays",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 400);

            migrationBuilder.AddColumn<bool>(
                name: "IsAnalyticsEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "TrafficCampaign",
                table: "Orders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrafficMedium",
                table: "Orders",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrafficSource",
                table: "Orders",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisitorKey",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AnalyticsCounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Metric = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalyticsCounters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VisitorDays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    VisitorKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Stages = table.Column<int>(type: "int", nullable: false),
                    PageViews = table.Column<int>(type: "int", nullable: false),
                    FirstHour = table.Column<byte>(type: "tinyint", nullable: false),
                    Device = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Medium = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Campaign = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LandingPath = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisitorDays", x => x.Id);
                    table.CheckConstraint("CK_VisitorDays_FirstHour", "[FirstHour] BETWEEN 0 AND 23");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_StoreSettings_Analytics",
                table: "StoreSettings",
                sql: "[AnalyticsRetentionDays] BETWEEN 30 AND 1500");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TrafficSource_CreatedAt",
                table: "Orders",
                columns: new[] { "TrafficSource", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_VisitorKey",
                table: "Orders",
                column: "VisitorKey",
                filter: "[VisitorKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsCounters_Day_Metric_Key",
                table: "AnalyticsCounters",
                columns: new[] { "Day", "Metric", "Key" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsCounters_Metric_Day",
                table: "AnalyticsCounters",
                columns: new[] { "Metric", "Day" })
                .Annotation("SqlServer:Include", new[] { "Key", "Count" });

            migrationBuilder.CreateIndex(
                name: "IX_VisitorDays_Day_Source_Medium",
                table: "VisitorDays",
                columns: new[] { "Day", "Source", "Medium" });

            migrationBuilder.CreateIndex(
                name: "IX_VisitorDays_Day_VisitorKey",
                table: "VisitorDays",
                columns: new[] { "Day", "VisitorKey" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_VisitorDays_VisitorKey",
                table: "VisitorDays",
                column: "VisitorKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalyticsCounters");

            migrationBuilder.DropTable(
                name: "VisitorDays");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StoreSettings_Analytics",
                table: "StoreSettings");

            migrationBuilder.DropIndex(
                name: "IX_Orders_TrafficSource_CreatedAt",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_VisitorKey",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "AnalyticsRetentionDays",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsAnalyticsEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "TrafficCampaign",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TrafficMedium",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TrafficSource",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "VisitorKey",
                table: "Orders");
        }
    }
}
