using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <summary>
    /// دور ۲۱ (دست‌نویس، چون CI امکان ساخت خودکار مهاجرت را ندارد): حذف کامل «دستیار محلی» و مدل زبانی (Ollama).
    /// <para>چهار ستون کلید/وضعیت دستیار از جدول SmartContentConfigs حذف می‌شوند؛ هیچ دادهٔ دیگری تغییر نمی‌کند.
    /// Down همان ستون‌ها را (خالی) برمی‌گرداند.</para>
    /// </summary>
    public partial class Round21Autonomy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "HelperKeyHash", table: "SmartContentConfigs");
            migrationBuilder.DropColumn(name: "HelperKeyHint", table: "SmartContentConfigs");
            migrationBuilder.DropColumn(name: "HelperLastSeenAt", table: "SmartContentConfigs");
            migrationBuilder.DropColumn(name: "HelperName", table: "SmartContentConfigs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "HelperKeyHash", table: "SmartContentConfigs", type: "nvarchar(128)", maxLength: 128, nullable: true);
            migrationBuilder.AddColumn<string>(name: "HelperKeyHint", table: "SmartContentConfigs", type: "nvarchar(20)", maxLength: 20, nullable: true);
            migrationBuilder.AddColumn<DateTimeOffset>(name: "HelperLastSeenAt", table: "SmartContentConfigs", type: "datetimeoffset", nullable: true);
            migrationBuilder.AddColumn<string>(name: "HelperName", table: "SmartContentConfigs", type: "nvarchar(100)", maxLength: 100, nullable: true);
        }
    }
}
