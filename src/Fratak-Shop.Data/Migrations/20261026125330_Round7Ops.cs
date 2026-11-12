using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round7Ops : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BackupFolder",
                table: "StoreSettings",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BackupKeepCount",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<int>(
                name: "BackupRunsPerDay",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "BackupStartHour",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<string>(
                name: "HealthCheckToken",
                table: "StoreSettings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IpAutoBlockHours",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 24);

            migrationBuilder.AddColumn<int>(
                name: "IpAutoBlockThreshold",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<bool>(
                name: "IsBackupEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBackupVerifyEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsIpAutoBlockEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsMaintenanceMode",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsUploadsBackupEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LogFileRetentionDays",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 14);

            migrationBuilder.AddColumn<string>(
                name: "MaintenanceAllowedIps",
                table: "StoreSettings",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MaintenanceMessage",
                table: "StoreSettings",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaintenanceRetryMinutes",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<string>(
                name: "AlertBaleChatId",
                table: "ReportDeliveryConfigs",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AlertEmailTo",
                table: "ReportDeliveryConfigs",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AlertTelegramChatId",
                table: "ReportDeliveryConfigs",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AlertEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Metric = table.Column<byte>(type: "tinyint", nullable: false),
                    Value = table.Column<double>(type: "float", nullable: false),
                    Threshold = table.Column<double>(type: "float", nullable: false),
                    IsResolved = table.Column<bool>(type: "bit", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AlertRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Metric = table.Column<byte>(type: "tinyint", nullable: false),
                    Threshold = table.Column<double>(type: "float", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CooldownMinutes = table.Column<int>(type: "int", nullable: false, defaultValue: 60),
                    SendExternal = table.Column<bool>(type: "bit", nullable: false),
                    LastTriggeredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastValue = table.Column<double>(type: "float", nullable: true),
                    LastCheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsFiring = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertRules", x => x.Id);
                    table.CheckConstraint("CK_AlertRules_Cooldown", "[CooldownMinutes] BETWEEN 5 AND 10080");
                });

            migrationBuilder.CreateTable(
                name: "BackgroundJobSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsPaused = table.Column<bool>(type: "bit", nullable: false),
                    PausedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PausedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackgroundJobSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BackupRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    IsManual = table.Column<bool>(type: "bit", nullable: false),
                    StartedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    SlotKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    FilePath = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsVerified = table.Column<bool>(type: "bit", nullable: false),
                    IsLocal = table.Column<bool>(type: "bit", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BotRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Pattern = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Hits = table.Column<long>(type: "bigint", nullable: false),
                    LastHitAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BotRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IpRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Action = table.Column<byte>(type: "tinyint", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsAutomatic = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Hits = table.Column<long>(type: "bigint", nullable: false),
                    LastHitAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IpRules", x => x.Id);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_StoreSettings_Ops",
                table: "StoreSettings",
                sql: "[BackupRunsPerDay] BETWEEN 1 AND 4 AND [BackupStartHour] BETWEEN 0 AND 23 AND [BackupKeepCount] BETWEEN 1 AND 60 AND [MaintenanceRetryMinutes] BETWEEN 5 AND 1440 AND [IpAutoBlockThreshold] BETWEEN 5 AND 1000 AND [IpAutoBlockHours] BETWEEN 1 AND 720 AND [LogFileRetentionDays] BETWEEN 1 AND 180");

            migrationBuilder.CreateIndex(
                name: "IX_AlertEvents_CreatedAt",
                table: "AlertEvents",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AlertRules_Metric",
                table: "AlertRules",
                column: "Metric",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundJobSettings_JobName",
                table: "BackgroundJobSettings",
                column: "JobName",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_BackupRecords_Kind_CreatedAt",
                table: "BackupRecords",
                columns: new[] { "Kind", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BackupRecords_Kind_SlotKey",
                table: "BackupRecords",
                columns: new[] { "Kind", "SlotKey" },
                unique: true,
                filter: "[SlotKey] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_BotRules_Pattern",
                table: "BotRules",
                column: "Pattern",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_IpRules_ExpiresAt",
                table: "IpRules",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_IpRules_Value",
                table: "IpRules",
                column: "Value",
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertEvents");

            migrationBuilder.DropTable(
                name: "AlertRules");

            migrationBuilder.DropTable(
                name: "BackgroundJobSettings");

            migrationBuilder.DropTable(
                name: "BackupRecords");

            migrationBuilder.DropTable(
                name: "BotRules");

            migrationBuilder.DropTable(
                name: "IpRules");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StoreSettings_Ops",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BackupFolder",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BackupKeepCount",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BackupRunsPerDay",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BackupStartHour",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "HealthCheckToken",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IpAutoBlockHours",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IpAutoBlockThreshold",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsBackupEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsBackupVerifyEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsIpAutoBlockEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsMaintenanceMode",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsUploadsBackupEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "LogFileRetentionDays",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "MaintenanceAllowedIps",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "MaintenanceMessage",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "MaintenanceRetryMinutes",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "AlertBaleChatId",
                table: "ReportDeliveryConfigs");

            migrationBuilder.DropColumn(
                name: "AlertEmailTo",
                table: "ReportDeliveryConfigs");

            migrationBuilder.DropColumn(
                name: "AlertTelegramChatId",
                table: "ReportDeliveryConfigs");
        }
    }
}
