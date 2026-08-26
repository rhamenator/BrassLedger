using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class SeparateSupplierReturnCreditValue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "VendorCreditAmount",
                table: "SupplierReturnShipments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VendorCreditAmount",
                table: "SupplierReturnShipmentLines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VendorCreditUnitCost",
                table: "SupplierReturnShipmentLines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ReceiptUnitCost",
                table: "SupplierReturnAuthorizationLines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Preserve posted historical return accounting while giving unshipped authorizations
            // the original goods-receipt cost needed for future vendor-credit calculations.
            migrationBuilder.Sql("""
                UPDATE "SupplierReturnAuthorizationLines" AS authorization_line
                SET "ReceiptUnitCost" = receipt_line."UnitCost"
                FROM "InventoryReceiptLines" AS receipt_line
                WHERE receipt_line."Id" = authorization_line."InventoryReceiptLineId";
                UPDATE "SupplierReturnShipmentLines"
                SET "VendorCreditUnitCost" = "UnitCost",
                    "VendorCreditAmount" = "TotalAmount";
                UPDATE "SupplierReturnShipments"
                SET "VendorCreditAmount" = "TotalAmount";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VendorCreditAmount",
                table: "SupplierReturnShipments");

            migrationBuilder.DropColumn(
                name: "VendorCreditAmount",
                table: "SupplierReturnShipmentLines");

            migrationBuilder.DropColumn(
                name: "VendorCreditUnitCost",
                table: "SupplierReturnShipmentLines");

            migrationBuilder.DropColumn(
                name: "ReceiptUnitCost",
                table: "SupplierReturnAuthorizationLines");
        }
    }
}
