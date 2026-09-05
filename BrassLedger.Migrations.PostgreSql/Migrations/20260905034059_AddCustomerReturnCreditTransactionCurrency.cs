using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerReturnCreditTransactionCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ExchangeRateEffectiveOn",
                table: "CustomerReturnCredits",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExchangeRateId",
                table: "CustomerReturnCredits",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExchangeRateSource",
                table: "CustomerReturnCredits",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExchangeRateSourceReference",
                table: "CustomerReturnCredits",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRateToBase",
                table: "CustomerReturnCredits",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionAppliedAmount",
                table: "CustomerReturnCredits",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "TransactionCurrency",
                table: "CustomerReturnCredits",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionSourceAppliedAmount",
                table: "CustomerReturnCredits",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionSubtotal",
                table: "CustomerReturnCredits",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionTaxAmount",
                table: "CustomerReturnCredits",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionTotalAmount",
                table: "CustomerReturnCredits",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionNetAmount",
                table: "CustomerReturnCreditLines",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionTaxAmount",
                table: "CustomerReturnCreditLines",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionTotalAmount",
                table: "CustomerReturnCreditLines",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionAmount",
                table: "CustomerReturnCreditApplications",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE "CustomerReturnCredits"
                SET "TransactionCurrency" = COALESCE((SELECT "BaseCurrency" FROM "Companies" WHERE "Companies"."Id" = "CustomerReturnCredits"."CompanyId"), 'USD'),
                    "TransactionSubtotal" = "Subtotal", "TransactionTaxAmount" = "TaxAmount", "TransactionTotalAmount" = "TotalAmount",
                    "TransactionSourceAppliedAmount" = "SourceAppliedAmount", "TransactionAppliedAmount" = "AppliedAmount",
                    "ExchangeRateToBase" = 1, "ExchangeRateEffectiveOn" = "CreditDate", "ExchangeRateSource" = 'Legacy base-currency document';
                UPDATE "CustomerReturnCreditLines" SET "TransactionNetAmount" = "NetAmount", "TransactionTaxAmount" = "TaxAmount", "TransactionTotalAmount" = "TotalAmount";
                UPDATE "CustomerReturnCreditApplications" SET "TransactionAmount" = "Amount";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Downgrade is prohibited because it could delete retained transaction-currency amounts and exchange-rate provenance on foreign-currency customer return credits. Restore a verified pre-upgrade backup instead.");
            /* The generated destructive downgrade is retained below for schema-review traceability only.
            migrationBuilder.DropColumn(
                name: "ExchangeRateEffectiveOn",
                table: "CustomerReturnCredits");

            migrationBuilder.DropColumn(
                name: "ExchangeRateId",
                table: "CustomerReturnCredits");

            migrationBuilder.DropColumn(
                name: "ExchangeRateSource",
                table: "CustomerReturnCredits");

            migrationBuilder.DropColumn(
                name: "ExchangeRateSourceReference",
                table: "CustomerReturnCredits");

            migrationBuilder.DropColumn(
                name: "ExchangeRateToBase",
                table: "CustomerReturnCredits");

            migrationBuilder.DropColumn(
                name: "TransactionAppliedAmount",
                table: "CustomerReturnCredits");

            migrationBuilder.DropColumn(
                name: "TransactionCurrency",
                table: "CustomerReturnCredits");

            migrationBuilder.DropColumn(
                name: "TransactionSourceAppliedAmount",
                table: "CustomerReturnCredits");

            migrationBuilder.DropColumn(
                name: "TransactionSubtotal",
                table: "CustomerReturnCredits");

            migrationBuilder.DropColumn(
                name: "TransactionTaxAmount",
                table: "CustomerReturnCredits");

            migrationBuilder.DropColumn(
                name: "TransactionTotalAmount",
                table: "CustomerReturnCredits");

            migrationBuilder.DropColumn(
                name: "TransactionNetAmount",
                table: "CustomerReturnCreditLines");

            migrationBuilder.DropColumn(
                name: "TransactionTaxAmount",
                table: "CustomerReturnCreditLines");

            migrationBuilder.DropColumn(
                name: "TransactionTotalAmount",
                table: "CustomerReturnCreditLines");

            migrationBuilder.DropColumn(
                name: "TransactionAmount",
                table: "CustomerReturnCreditApplications");
            */
        }
    }
}
