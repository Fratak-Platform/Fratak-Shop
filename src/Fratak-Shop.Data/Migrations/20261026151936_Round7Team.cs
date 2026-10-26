using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round7Team : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TotpEnabledAt",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TotpLastStep",
                table: "Users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TotpRecoveryCodes",
                table: "Users",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TotpSecretProtected",
                table: "Users",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDataMaskingEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsReauthEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsStaffTotpEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsStaffTotpRequired",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsTwoPersonApprovalEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsTwoPersonRoleChangeEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "ReauthMinutes",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<decimal>(
                name: "TwoPersonWalletThreshold",
                table: "StoreSettings",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 1000000m);

            migrationBuilder.AddColumn<decimal>(
                name: "TwoPersonWithdrawalThreshold",
                table: "StoreSettings",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 5000000m);

            migrationBuilder.CreateTable(
                name: "ApprovalRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: true),
                    TargetUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    TargetKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RequestedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    DecidedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ResultMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StaffTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssigneeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Priority = table.Column<byte>(type: "tinyint", nullable: false),
                    IsDone = table.Column<bool>(type: "bit", nullable: false),
                    DoneAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Url = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ReminderSentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffTasks", x => x.Id);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_StoreSettings_Team",
                table: "StoreSettings",
                sql: "[ReauthMinutes] BETWEEN 5 AND 240 AND [TwoPersonWalletThreshold] >= 0 AND [TwoPersonWithdrawalThreshold] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_RequestedById_CreatedAt",
                table: "ApprovalRequests",
                columns: new[] { "RequestedById", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_Status_CreatedAt",
                table: "ApprovalRequests",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_TargetKey_Status",
                table: "ApprovalRequests",
                columns: new[] { "TargetKey", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffTasks_AssigneeId_IsDone_DueAt",
                table: "StaffTasks",
                columns: new[] { "AssigneeId", "IsDone", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffTasks_IsDone_DueAt_ReminderSentAt",
                table: "StaffTasks",
                columns: new[] { "IsDone", "DueAt", "ReminderSentAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalRequests");

            migrationBuilder.DropTable(
                name: "StaffTasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StoreSettings_Team",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "TotpEnabledAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TotpLastStep",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TotpRecoveryCodes",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TotpSecretProtected",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsDataMaskingEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsReauthEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsStaffTotpEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsStaffTotpRequired",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsTwoPersonApprovalEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsTwoPersonRoleChangeEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReauthMinutes",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "TwoPersonWalletThreshold",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "TwoPersonWithdrawalThreshold",
                table: "StoreSettings");
        }
    }
}
