using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round10BrandPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BrandAccentColor",
                table: "StoreSettings",
                type: "nvarchar(9)",
                maxLength: 9,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrandTextAccent",
                table: "StoreSettings",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrandTextColor",
                table: "StoreSettings",
                type: "nvarchar(9)",
                maxLength: 9,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BrandTextInAdmin",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "BrandTextInAuth",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "BrandTextInHeader",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "BrandTextInInvoice",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "BrandTextPrimary",
                table: "StoreSettings",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCompetitorPriceLookupEnabled",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoDarkUrl",
                table: "StoreSettings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PricingGatewayFeePercent",
                table: "StoreSettings",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<int>(
                name: "PricingMinMarginPercent",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 8);

            migrationBuilder.AddColumn<decimal>(
                name: "PricingOtherCostAmount",
                table: "StoreSettings",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PricingOtherCostPercent",
                table: "StoreSettings",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PricingPackagingCost",
                table: "StoreSettings",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PricingRounding",
                table: "StoreSettings",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 1000m);

            migrationBuilder.AddColumn<decimal>(
                name: "PricingShippingCost",
                table: "StoreSettings",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "PricingTargetMarginPercent",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<decimal>(
                name: "PricingTaxPercent",
                table: "StoreSettings",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PricingUndercutPercent",
                table: "StoreSettings",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 2m);

            migrationBuilder.AddColumn<string>(
                name: "PwaIconUrl",
                table: "StoreSettings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SupplierPriceAutoMode",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<decimal>(
                name: "SupplierRepriceMarkupAmount",
                table: "StoreSettings",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "SupplierRepriceMarkupPercent",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 25);

            migrationBuilder.AddColumn<int>(
                name: "SupplierRepriceMaxJumpPercent",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 50);

            migrationBuilder.AddColumn<int>(
                name: "SupplierRepriceMinMarginPercent",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "SupplierRepriceMode",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "SupplierRepriceOnDecrease",
                table: "StoreSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "SupplierRepriceRounding",
                table: "StoreSettings",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 1000m);

            migrationBuilder.AddCheckConstraint(
                name: "CK_StoreSettings_Round10",
                table: "StoreSettings",
                sql: "[SupplierPriceAutoMode] BETWEEN 0 AND 2 AND [SupplierRepriceMode] BETWEEN 0 AND 5 AND [SupplierRepriceMarkupPercent] BETWEEN 0 AND 1000 AND [SupplierRepriceMarkupAmount] >= 0 AND [SupplierRepriceMinMarginPercent] BETWEEN 0 AND 500 AND [SupplierRepriceRounding] >= 0 AND [SupplierRepriceMaxJumpPercent] BETWEEN 0 AND 1000 AND [PricingPackagingCost] >= 0 AND [PricingShippingCost] >= 0 AND [PricingOtherCostAmount] >= 0 AND [PricingGatewayFeePercent] BETWEEN 0 AND 20 AND [PricingOtherCostPercent] BETWEEN 0 AND 50 AND [PricingTaxPercent] BETWEEN 0 AND 30 AND [PricingTargetMarginPercent] BETWEEN 0 AND 500 AND [PricingMinMarginPercent] BETWEEN 0 AND 500 AND [PricingUndercutPercent] BETWEEN 0 AND 30 AND [PricingRounding] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StoreSettings_Round10",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BrandAccentColor",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BrandTextAccent",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BrandTextColor",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BrandTextInAdmin",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BrandTextInAuth",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BrandTextInHeader",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BrandTextInInvoice",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BrandTextPrimary",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "IsCompetitorPriceLookupEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "LogoDarkUrl",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "PricingGatewayFeePercent",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "PricingMinMarginPercent",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "PricingOtherCostAmount",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "PricingOtherCostPercent",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "PricingPackagingCost",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "PricingRounding",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "PricingShippingCost",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "PricingTargetMarginPercent",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "PricingTaxPercent",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "PricingUndercutPercent",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "PwaIconUrl",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "SupplierPriceAutoMode",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "SupplierRepriceMarkupAmount",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "SupplierRepriceMarkupPercent",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "SupplierRepriceMaxJumpPercent",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "SupplierRepriceMinMarginPercent",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "SupplierRepriceMode",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "SupplierRepriceOnDecrease",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "SupplierRepriceRounding",
                table: "StoreSettings");
        }
    }
}
