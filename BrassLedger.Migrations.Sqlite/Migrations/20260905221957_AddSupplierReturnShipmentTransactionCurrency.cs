using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierReturnShipmentTransactionCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ExchangeRateEffectiveOn",
                table: "SupplierReturnShipments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExchangeRateId",
                table: "SupplierReturnShipments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExchangeRateSource",
                table: "SupplierReturnShipments",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExchangeRateSourceReference",
                table: "SupplierReturnShipments",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRateToBase",
                table: "SupplierReturnShipments",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionAppliedAmount",
                table: "SupplierReturnShipments",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "TransactionCurrency",
                table: "SupplierReturnShipments",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionSourceAppliedAmount",
                table: "SupplierReturnShipments",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionVendorCreditAmount",
                table: "SupplierReturnShipments",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionVendorCreditAmount",
                table: "SupplierReturnShipmentLines",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionVendorCreditUnitCost",
                table: "SupplierReturnShipmentLines",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionAmount",
                table: "SupplierReturnCreditApplications",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE "SupplierReturnShipments"
                SET "TransactionCurrency" = COALESCE((SELECT "BaseCurrency" FROM "Companies" WHERE "Companies"."Id" = "SupplierReturnShipments"."CompanyId"), 'USD'),
                    "TransactionVendorCreditAmount" = "VendorCreditAmount",
                    "TransactionSourceAppliedAmount" = "SourceAppliedAmount", "TransactionAppliedAmount" = "AppliedAmount",
                    "ExchangeRateToBase" = 1, "ExchangeRateEffectiveOn" = "ShippedOn", "ExchangeRateSource" = 'Legacy base-currency document';
                UPDATE "SupplierReturnShipmentLines" SET "TransactionVendorCreditUnitCost" = "VendorCreditUnitCost", "TransactionVendorCreditAmount" = "VendorCreditAmount";
                UPDATE "SupplierReturnCreditApplications" SET "TransactionAmount" = "Amount";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Downgrade is prohibited because it could delete retained transaction-currency amounts and exchange-rate provenance on foreign-currency supplier return credits. Restore a verified pre-upgrade backup instead.");
            /* The generated destructive downgrade is retained below for schema-review traceability only.
            migrationBuilder.DropColumn(
                name: "ExchangeRateEffectiveOn",
                table: "SupplierReturnShipments");

            migrationBuilder.DropColumn(
                name: "ExchangeRateId",
                table: "SupplierReturnShipments");

            migrationBuilder.DropColumn(
                name: "ExchangeRateSource",
                table: "SupplierReturnShipments");

            migrationBuilder.DropColumn(
                name: "ExchangeRateSourceReference",
                table: "SupplierReturnShipments");

            migrationBuilder.DropColumn(
                name: "ExchangeRateToBase",
                table: "SupplierReturnShipments");

            migrationBuilder.DropColumn(
                name: "TransactionAppliedAmount",
                table: "SupplierReturnShipments");

            migrationBuilder.DropColumn(
                name: "TransactionCurrency",
                table: "SupplierReturnShipments");

            migrationBuilder.DropColumn(
                name: "TransactionSourceAppliedAmount",
                table: "SupplierReturnShipments");

            migrationBuilder.DropColumn(
                name: "TransactionVendorCreditAmount",
                table: "SupplierReturnShipments");

            migrationBuilder.DropColumn(
                name: "TransactionVendorCreditAmount",
                table: "SupplierReturnShipmentLines");

            migrationBuilder.DropColumn(
                name: "TransactionVendorCreditUnitCost",
                table: "SupplierReturnShipmentLines");

            migrationBuilder.DropColumn(
                name: "TransactionAmount",
                table: "SupplierReturnCreditApplications");
            */
        }
    }
}
