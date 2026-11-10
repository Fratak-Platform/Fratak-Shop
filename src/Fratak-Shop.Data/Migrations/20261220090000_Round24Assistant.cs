using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <summary>
    /// دور ۲۴ (دست‌نویس): جدول‌های تازهٔ دستیار فروشگاه.
    /// AssistantProfiles (سلیقهٔ گفت‌وگوی هر کاربر)، AssistantHandoffs و AssistantHandoffMessages (گفت‌وگو با کارشناس).
    /// Down هر سه جدول را برمی‌دارد.
    /// </summary>
    public partial class Round24Assistant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssistantProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CallMe = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Tone = table.Column<byte>(type: "tinyint", nullable: false),
                    Register = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssistantHandoffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    PageUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    StaffUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StaffName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    LastMessageAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UnreadForStaff = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantHandoffs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssistantHandoffMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HandoffId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromStaff = table.Column<bool>(type: "bit", nullable: false),
                    AuthorName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantHandoffMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistantHandoffMessages_AssistantHandoffs_HandoffId",
                        column: x => x.HandoffId,
                        principalTable: "AssistantHandoffs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantProfiles_UserId",
                table: "AssistantProfiles",
                column: "UserId",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantHandoffs_SessionKey_Status",
                table: "AssistantHandoffs",
                columns: new[] { "SessionKey", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantHandoffs_Status_LastMessageAt",
                table: "AssistantHandoffs",
                columns: new[] { "Status", "LastMessageAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantHandoffMessages_HandoffId_CreatedAt",
                table: "AssistantHandoffMessages",
                columns: new[] { "HandoffId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AssistantHandoffMessages");
            migrationBuilder.DropTable(name: "AssistantHandoffs");
            migrationBuilder.DropTable(name: "AssistantProfiles");
        }
    }
}
