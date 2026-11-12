using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round7Supplier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_PaymentToken",
                table: "Orders");

            migrationBuilder.AddColumn<int>(
                name: "SyncMode",
                table: "Suppliers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsSupplierSyncEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "SupplierAutoOutOfStock",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "SupplierRequestDelayMs",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 1500);

            migrationBuilder.AddColumn<int>(
                name: "SupplierStockMarginPercent",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<int>(
                name: "SupplierStockMaxQuantity",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<int>(
                name: "SupplierSyncRunsPerDay",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.AddColumn<bool>(
                name: "ExcludeFromSupplierSync",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "SupplierChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OldValue = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: true),
                    NewValue = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: true),
                    SourceStock = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierChanges_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupplierProductStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ResolvedUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SourceName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SourcePrice = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: true),
                    SourceStock = table.Column<int>(type: "int", nullable: true),
                    CheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastSuccessAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConsecutiveErrors = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AutoOutOfStock = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierProductStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierProductStates_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupplierSyncRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Trigger = table.Column<int>(type: "int", nullable: false),
                    StartedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Checked = table.Column<int>(type: "int", nullable: false),
                    InStock = table.Column<int>(type: "int", nullable: false),
                    OutOfStock = table.Column<int>(type: "int", nullable: false),
                    NotFound = table.Column<int>(type: "int", nullable: false),
                    Errors = table.Column<int>(type: "int", nullable: false),
                    AutoApplied = table.Column<int>(type: "int", nullable: false),
                    NewProposals = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierSyncRuns", x => x.Id);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_StoreSettings_SupplierSync",
                table: "StoreSettings",
                sql: "[SupplierSyncRunsPerDay] BETWEEN 1 AND 24 AND [SupplierStockMarginPercent] BETWEEN 0 AND 90 AND [SupplierStockMaxQuantity] >= 1 AND [SupplierRequestDelayMs] >= 200");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PaymentToken",
                table: "Orders",
                column: "PaymentToken",
                unique: true,
                filter: "[PaymentToken] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierChanges_ProductId_Kind_Status",
                table: "SupplierChanges",
                columns: new[] { "ProductId", "Kind", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierChanges_Status_Kind_CreatedAt",
                table: "SupplierChanges",
                columns: new[] { "Status", "Kind", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierProductStates_ProductId",
                table: "SupplierProductStates",
                column: "ProductId",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierProductStates_Status_ConsecutiveErrors",
                table: "SupplierProductStates",
                columns: new[] { "Status", "ConsecutiveErrors" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierSyncRuns_StartedAt",
                table: "SupplierSyncRuns",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupplierChanges");

            migrationBuilder.DropTable(
                name: "SupplierProductStates");

            migrationBuilder.DropTable(
                name: "SupplierSyncRuns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StoreSettings_SupplierSync",
                table: "StoreSettings");

            migrationBuilder.DropIndex(
                name: "IX_Orders_PaymentToken",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SyncMode",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "IsSupplierSyncEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "SupplierAutoOutOfStock",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "SupplierRequestDelayMs",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "SupplierStockMarginPercent",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "SupplierStockMaxQuantity",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "SupplierSyncRunsPerDay",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ExcludeFromSupplierSync",
                table: "Products");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PaymentToken",
                table: "Orders",
                column: "PaymentToken",
                unique: true,
                filter: "[IsDeleted] = 0");
        }
    }
}
