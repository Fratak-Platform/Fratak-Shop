using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round7Orders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CodAllowedProvinces",
                table: "StoreSettings",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CodMaxReturnedOrders",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CodMinDeliveredOrders",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsOrderSlaEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentLinkHours",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 48);

            migrationBuilder.AddColumn<int>(
                name: "SlaDeliveryMaxDays",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.AddColumn<int>(
                name: "SlaDeliveryMinDays",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "SlaPreparationHours",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 48);

            migrationBuilder.AddColumn<int>(
                name: "SlaSupplierOrderHours",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 24);

            migrationBuilder.AddColumn<int>(
                name: "SlaTrackingCodeHours",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 12);

            migrationBuilder.AddColumn<int>(
                name: "SlaWarningPercent",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 75);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByStaffId",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedByStaffName",
                table: "Orders",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentToken",
                table: "Orders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProcessingAt",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ShippedAt",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SlaAlertedStage",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CustomerNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AuthorName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Content = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    IsPinned = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerNotes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomerTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tag = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreatedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerTags_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromStaff = table.Column<bool>(type: "bit", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AuthorName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ItemName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Decision = table.Column<int>(type: "int", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReadByCustomerAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReadByStaffAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderMessages_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_StoreSettings_Round7Orders",
                table: "StoreSettings",
                sql: "[SlaSupplierOrderHours] >= 1 AND [SlaPreparationHours] >= 1 AND [SlaTrackingCodeHours] >= 1 AND [SlaDeliveryMinDays] >= 0 AND [SlaDeliveryMaxDays] >= [SlaDeliveryMinDays] AND [PaymentLinkHours] >= 1 AND [CodMinDeliveredOrders] >= 0 AND [CodMaxReturnedOrders] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PaymentToken",
                table: "Orders",
                column: "PaymentToken",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerNotes_UserId_IsPinned_CreatedAt",
                table: "CustomerNotes",
                columns: new[] { "UserId", "IsPinned", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerTags_Tag",
                table: "CustomerTags",
                column: "Tag");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerTags_UserId_Tag",
                table: "CustomerTags",
                columns: new[] { "UserId", "Tag" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OrderMessages_FromStaff_ReadByStaffAt",
                table: "OrderMessages",
                columns: new[] { "FromStaff", "ReadByStaffAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderMessages_OrderId_CreatedAt",
                table: "OrderMessages",
                columns: new[] { "OrderId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerNotes");

            migrationBuilder.DropTable(
                name: "CustomerTags");

            migrationBuilder.DropTable(
                name: "OrderMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StoreSettings_Round7Orders",
                table: "StoreSettings");

            migrationBuilder.DropIndex(
                name: "IX_Orders_PaymentToken",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CodAllowedProvinces",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "CodMaxReturnedOrders",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "CodMinDeliveredOrders",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsOrderSlaEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "PaymentLinkHours",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "SlaDeliveryMaxDays",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "SlaDeliveryMinDays",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "SlaPreparationHours",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "SlaSupplierOrderHours",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "SlaTrackingCodeHours",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "SlaWarningPercent",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "CreatedByStaffId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CreatedByStaffName",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentToken",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ProcessingAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SlaAlertedStage",
                table: "Orders");
        }
    }
}
