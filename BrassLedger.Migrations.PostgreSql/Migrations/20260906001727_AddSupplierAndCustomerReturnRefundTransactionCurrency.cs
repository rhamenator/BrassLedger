using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierAndCustomerReturnRefundTransactionCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TransactionRefundedAmount",
                table: "SupplierReturnShipments",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExchangeRateEffectiveOn",
                table: "SupplierReturnCreditRefunds",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExchangeRateId",
                table: "SupplierReturnCreditRefunds",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExchangeRateSource",
                table: "SupplierReturnCreditRefunds",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExchangeRateSourceReference",
                table: "SupplierReturnCreditRefunds",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRateToBase",
                table: "SupplierReturnCreditRefunds",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RealizedGainLoss",
                table: "SupplierReturnCreditRefunds",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionAmount",
                table: "SupplierReturnCreditRefunds",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionRefundedAmount",
                table: "CustomerReturnCredits",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExchangeRateEffectiveOn",
                table: "CustomerReturnCreditRefunds",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExchangeRateId",
                table: "CustomerReturnCreditRefunds",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExchangeRateSource",
                table: "CustomerReturnCreditRefunds",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExchangeRateSourceReference",
                table: "CustomerReturnCreditRefunds",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRateToBase",
                table: "CustomerReturnCreditRefunds",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RealizedGainLoss",
                table: "CustomerReturnCreditRefunds",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionAmount",
                table: "CustomerReturnCreditRefunds",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE "SupplierReturnShipments" SET "TransactionRefundedAmount" = "RefundedAmount";
                UPDATE "SupplierReturnCreditRefunds"
                SET "TransactionAmount" = "Amount", "ExchangeRateToBase" = 1, "ExchangeRateSource" = 'Legacy base-currency refund';
                UPDATE "CustomerReturnCredits" SET "TransactionRefundedAmount" = "RefundedAmount";
                UPDATE "CustomerReturnCreditRefunds"
                SET "TransactionAmount" = "Amount", "ExchangeRateToBase" = 1, "ExchangeRateSource" = 'Legacy base-currency refund';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Downgrade is prohibited because it could delete retained transaction-currency amounts and exchange-rate provenance on foreign-currency return-credit refunds. Restore a verified pre-upgrade backup instead.");
            /* The generated destructive downgrade is retained below for schema-review traceability only.
            migrationBuilder.DropColumn(
                name: "TransactionRefundedAmount",
                table: "SupplierReturnShipments");

            migrationBuilder.DropColumn(
                name: "ExchangeRateEffectiveOn",
                table: "SupplierReturnCreditRefunds");

            migrationBuilder.DropColumn(
                name: "ExchangeRateId",
                table: "SupplierReturnCreditRefunds");

            migrationBuilder.DropColumn(
                name: "ExchangeRateSource",
                table: "SupplierReturnCreditRefunds");

            migrationBuilder.DropColumn(
                name: "ExchangeRateSourceReference",
                table: "SupplierReturnCreditRefunds");

            migrationBuilder.DropColumn(
                name: "ExchangeRateToBase",
                table: "SupplierReturnCreditRefunds");

            migrationBuilder.DropColumn(
                name: "RealizedGainLoss",
                table: "SupplierReturnCreditRefunds");

            migrationBuilder.DropColumn(
                name: "TransactionAmount",
                table: "SupplierReturnCreditRefunds");

            migrationBuilder.DropColumn(
                name: "TransactionRefundedAmount",
                table: "CustomerReturnCredits");

            migrationBuilder.DropColumn(
                name: "ExchangeRateEffectiveOn",
                table: "CustomerReturnCreditRefunds");

            migrationBuilder.DropColumn(
                name: "ExchangeRateId",
                table: "CustomerReturnCreditRefunds");

            migrationBuilder.DropColumn(
                name: "ExchangeRateSource",
                table: "CustomerReturnCreditRefunds");

            migrationBuilder.DropColumn(
                name: "ExchangeRateSourceReference",
                table: "CustomerReturnCreditRefunds");

            migrationBuilder.DropColumn(
                name: "ExchangeRateToBase",
                table: "CustomerReturnCreditRefunds");

            migrationBuilder.DropColumn(
                name: "RealizedGainLoss",
                table: "CustomerReturnCreditRefunds");

            migrationBuilder.DropColumn(
                name: "TransactionAmount",
                table: "CustomerReturnCreditRefunds");
            */
        }
    }
}
