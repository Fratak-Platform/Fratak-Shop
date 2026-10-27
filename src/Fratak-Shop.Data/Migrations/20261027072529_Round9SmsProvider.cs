using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round9SmsProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SmsProviderConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<int>(type: "int", nullable: false),
                    ApiKeyProtected = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LineNumber = table.Column<long>(type: "bigint", nullable: true),
                    OtpTemplateId = table.Column<int>(type: "int", nullable: true),
                    OtpParameterName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsProviderConfigs", x => x.Id);
                    table.CheckConstraint("CK_SmsProviderConfigs_Line", "[LineNumber] IS NULL OR [LineNumber] > 0");
                    table.CheckConstraint("CK_SmsProviderConfigs_Template", "[OtpTemplateId] IS NULL OR [OtpTemplateId] > 0");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SmsProviderConfigs");
        }
    }
}
