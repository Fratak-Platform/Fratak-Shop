using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <summary>
    /// مرحلهٔ دوازدهم (دست‌نویس، چون CI امکان ساخت خودکار مهاجرت را نداشت):
    /// استودیوی لوگو (StoreSettings.BrandStyleJson)، ایمیل برای اعلان‌ها (NotificationTemplates.SendEmail)
    /// و جدول تنظیمات سرویس ایمیل (EmailProviderConfigs).
    /// </summary>
    public partial class Round12BrandEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BrandStyleJson",
                table: "StoreSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SendEmail",
                table: "NotificationTemplates",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "EmailProviderConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    Preset = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Host = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Port = table.Column<int>(type: "int", nullable: false, defaultValue: 587),
                    Security = table.Column<int>(type: "int", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PasswordProtected = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    FromAddress = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FromName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReplyTo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SiteBaseUrl = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MaxPerHour = table.Column<int>(type: "int", nullable: false, defaultValue: 100),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailProviderConfigs", x => x.Id);
                    table.CheckConstraint("CK_EmailProviderConfigs_MaxPerHour", "[MaxPerHour] >= 0");
                    table.CheckConstraint("CK_EmailProviderConfigs_Port", "[Port] BETWEEN 1 AND 65535");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailProviderConfigs");

            migrationBuilder.DropColumn(
                name: "SendEmail",
                table: "NotificationTemplates");

            migrationBuilder.DropColumn(
                name: "BrandStyleJson",
                table: "StoreSettings");
        }
    }
}
