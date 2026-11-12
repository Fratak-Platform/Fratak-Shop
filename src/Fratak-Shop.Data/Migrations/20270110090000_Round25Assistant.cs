using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <summary>
    /// دور ۲۵ (دست‌نویس): دستیار حرفه‌ای‌تر و اعلان ارزان شدن علاقه‌مندی‌ها.
    /// <list type="bullet">
    /// <item>AssistantMessages.Helpful – نظر 👍/👎 مشتری دربارهٔ هر پاسخ.</item>
    /// <item>جدول AssistantTeachings – جواب‌هایی که کارکنان به دستیار یاد می‌دهند.</item>
    /// <item>Favorites.WatchPrice و PriceAlertedAt – قیمت مرجع علاقه‌مندی و زمان آخرین اعلان «ارزان شد».</item>
    /// </list>
    /// Down همه را برمی‌دارد.
    /// </summary>
    public partial class Round25Assistant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Helpful",
                table: "AssistantMessages",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WatchPrice",
                table: "Favorites",
                type: "decimal(18,0)",
                precision: 18,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PriceAlertedAt",
                table: "Favorites",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssistantTeachings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Question = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Answer = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Url = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Intent = table.Column<byte>(type: "tinyint", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    HitCount = table.Column<int>(type: "int", nullable: false),
                    LastHitAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedByName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantTeachings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantTeachings_IsActive",
                table: "AssistantTeachings",
                column: "IsActive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssistantTeachings");

            migrationBuilder.DropColumn(
                name: "Helpful",
                table: "AssistantMessages");

            migrationBuilder.DropColumn(
                name: "WatchPrice",
                table: "Favorites");

            migrationBuilder.DropColumn(
                name: "PriceAlertedAt",
                table: "Favorites");
        }
    }
}
