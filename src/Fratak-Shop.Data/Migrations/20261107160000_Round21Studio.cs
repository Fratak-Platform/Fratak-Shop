using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <summary>
    /// دور ۲۱ (دست‌نویس): استودیوی طراحی، صفحه‌ساز کمپین و رشد خودکار.
    /// <list type="bullet">
    /// <item>DesignDocs: طرح‌های قابل ویرایش (JSON ویرایشگر + خروجی WebP)، ایده‌های سامانه با کلید یکتا.</item>
    /// <item>LandingPages: صفحه‌های کمپین (HTML/CSS پاک‌سازی‌شده + دادهٔ ویرایشگر)، نامک یکتا با فیلتر حذف نرم.</item>
    /// <item>GrowthActions: گزارش کارهایی که سامانه بدون انسان برای رشد بازدید و سئو انجام داد.</item>
    /// </list>
    /// هیچ جدول موجودی تغییر نمی‌کند؛ Down سه جدول را حذف می‌کند.
    /// </summary>
    public partial class Round21Studio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DesignDocs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    IsSmart = table.Column<bool>(type: "bit", nullable: false),
                    PresetKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    Json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImageName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MobileJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MobileImageName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IdeaKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    IdeaNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Headline = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Subline = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ButtonText = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    LinkUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BannerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BlogPostId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_DesignDocs", x => x.Id));

            migrationBuilder.CreateTable(
                name: "LandingPages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    MetaTitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    MetaDescription = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ProjectJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Html = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Css = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ShowHeader = table.Column<bool>(type: "bit", nullable: false),
                    ShowFooter = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    StartAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TemplateKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    UpdatedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_LandingPages", x => x.Id));

            migrationBuilder.CreateTable(
                name: "GrowthActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false),
                    Metric = table.Column<int>(type: "int", nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_GrowthActions", x => x.Id));

            migrationBuilder.CreateIndex(name: "IX_DesignDocs_BlogPostId", table: "DesignDocs", column: "BlogPostId");
            migrationBuilder.CreateIndex(name: "IX_DesignDocs_IdeaKey", table: "DesignDocs", column: "IdeaKey");
            migrationBuilder.CreateIndex(name: "IX_DesignDocs_Status_Kind", table: "DesignDocs", columns: new[] { "Status", "Kind" });
            migrationBuilder.CreateIndex(name: "IX_LandingPages_Slug", table: "LandingPages", column: "Slug", unique: true, filter: "[IsDeleted] = 0");
            migrationBuilder.CreateIndex(name: "IX_GrowthActions_Day_Kind", table: "GrowthActions", columns: new[] { "Day", "Kind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DesignDocs");
            migrationBuilder.DropTable(name: "LandingPages");
            migrationBuilder.DropTable(name: "GrowthActions");
        }
    }
}
