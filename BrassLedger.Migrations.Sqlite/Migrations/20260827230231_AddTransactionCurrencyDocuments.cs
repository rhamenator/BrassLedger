using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionCurrencyDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ExchangeRateEffectiveOn",
                table: "VendorBills",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExchangeRateId",
                table: "VendorBills",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExchangeRateSource",
                table: "VendorBills",
                type: "TEXT",
                maxLength: 240,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExchangeRateSourceReference",
                table: "VendorBills",
                type: "TEXT",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRateToBase",
                table: "VendorBills",
                type: "TEXT",
                precision: 18,
                scale: 10,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionBalanceDue",
                table: "VendorBills",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "TransactionCurrency",
                table: "VendorBills",
                type: "TEXT",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionTotalAmount",
                table: "VendorBills",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BaseLineTotal",
                table: "VendorBillLines",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BaseTaxAmount",
                table: "VendorBillLines",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExchangeRateEffectiveOn",
                table: "SubledgerPayments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExchangeRateId",
                table: "SubledgerPayments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExchangeRateSource",
                table: "SubledgerPayments",
                type: "TEXT",
                maxLength: 240,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExchangeRateSourceReference",
                table: "SubledgerPayments",
                type: "TEXT",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRateToBase",
                table: "SubledgerPayments",
                type: "TEXT",
                precision: 18,
                scale: 10,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RealizedGainLoss",
                table: "SubledgerPayments",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionAmount",
                table: "SubledgerPayments",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionAppliedAmount",
                table: "SubledgerPayments",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "TransactionCurrency",
                table: "SubledgerPayments",
                type: "TEXT",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionUnappliedAmount",
                table: "SubledgerPayments",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RealizedGainLoss",
                table: "SubledgerPaymentApplications",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionAmount",
                table: "SubledgerPaymentApplications",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExchangeRateEffectiveOn",
                table: "SalesInvoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExchangeRateId",
                table: "SalesInvoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExchangeRateSource",
                table: "SalesInvoices",
                type: "TEXT",
                maxLength: 240,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExchangeRateSourceReference",
                table: "SalesInvoices",
                type: "TEXT",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRateToBase",
                table: "SalesInvoices",
                type: "TEXT",
                precision: 18,
                scale: 10,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionBalanceDue",
                table: "SalesInvoices",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "TransactionCurrency",
                table: "SalesInvoices",
                type: "TEXT",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionSubtotal",
                table: "SalesInvoices",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionTaxAmount",
                table: "SalesInvoices",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionTotalAmount",
                table: "SalesInvoices",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BaseLineTotal",
                table: "SalesInvoiceLines",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BaseTaxAmount",
                table: "SalesInvoiceLines",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE "SalesInvoices"
                SET "TransactionCurrency" = COALESCE((SELECT "BaseCurrency" FROM "Companies" WHERE "Companies"."Id" = "SalesInvoices"."CompanyId"), 'USD'),
                    "TransactionSubtotal" = "Subtotal", "TransactionTaxAmount" = "TaxAmount", "TransactionTotalAmount" = "TotalAmount", "TransactionBalanceDue" = "BalanceDue",
                    "ExchangeRateToBase" = 1, "ExchangeRateEffectiveOn" = "InvoiceDate", "ExchangeRateSource" = 'Legacy base-currency document';
                UPDATE "SalesInvoiceLines" SET "BaseTaxAmount" = "TaxAmount", "BaseLineTotal" = "LineTotal";
                UPDATE "VendorBills"
                SET "TransactionCurrency" = COALESCE((SELECT "BaseCurrency" FROM "Companies" WHERE "Companies"."Id" = "VendorBills"."CompanyId"), 'USD'),
                    "TransactionTotalAmount" = "TotalAmount", "TransactionBalanceDue" = "BalanceDue", "ExchangeRateToBase" = 1,
                    "ExchangeRateEffectiveOn" = "BillDate", "ExchangeRateSource" = 'Legacy base-currency document';
                UPDATE "VendorBillLines" SET "BaseTaxAmount" = "TaxAmount", "BaseLineTotal" = "LineTotal";
                UPDATE "SubledgerPayments"
                SET "TransactionCurrency" = COALESCE((SELECT "BaseCurrency" FROM "Companies" WHERE "Companies"."Id" = "SubledgerPayments"."CompanyId"), 'USD'),
                    "TransactionAmount" = "Amount", "TransactionAppliedAmount" = "AppliedAmount", "TransactionUnappliedAmount" = "UnappliedAmount",
                    "ExchangeRateToBase" = 1, "ExchangeRateEffectiveOn" = "PaymentDate", "ExchangeRateSource" = 'Legacy base-currency payment';
                UPDATE "SubledgerPaymentApplications" SET "TransactionAmount" = "Amount";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_VendorBills_ExchangeRateId",
                table: "VendorBills",
                column: "ExchangeRateId");

            migrationBuilder.CreateIndex(
                name: "IX_SubledgerPayments_ExchangeRateId",
                table: "SubledgerPayments",
                column: "ExchangeRateId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_ExchangeRateId",
                table: "SalesInvoices",
                column: "ExchangeRateId");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoices_CurrencyExchangeRates_ExchangeRateId",
                table: "SalesInvoices",
                column: "ExchangeRateId",
                principalTable: "CurrencyExchangeRates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SubledgerPayments_CurrencyExchangeRates_ExchangeRateId",
                table: "SubledgerPayments",
                column: "ExchangeRateId",
                principalTable: "CurrencyExchangeRates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VendorBills_CurrencyExchangeRates_ExchangeRateId",
                table: "VendorBills",
                column: "ExchangeRateId",
                principalTable: "CurrencyExchangeRates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Downgrade is prohibited because it could delete retained transaction-currency amounts, exchange-rate provenance, and realized foreign-exchange accounting evidence. Restore a verified pre-upgrade backup instead.");
            /* The generated destructive downgrade is retained below for schema-review traceability only.
            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoices_CurrencyExchangeRates_ExchangeRateId",
                table: "SalesInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_SubledgerPayments_CurrencyExchangeRates_ExchangeRateId",
                table: "SubledgerPayments");

            migrationBuilder.DropForeignKey(
                name: "FK_VendorBills_CurrencyExchangeRates_ExchangeRateId",
                table: "VendorBills");

            migrationBuilder.DropIndex(
                name: "IX_VendorBills_ExchangeRateId",
                table: "VendorBills");

            migrationBuilder.DropIndex(
                name: "IX_SubledgerPayments_ExchangeRateId",
                table: "SubledgerPayments");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoices_ExchangeRateId",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "ExchangeRateEffectiveOn",
                table: "VendorBills");

            migrationBuilder.DropColumn(
                name: "ExchangeRateId",
                table: "VendorBills");

            migrationBuilder.DropColumn(
                name: "ExchangeRateSource",
                table: "VendorBills");

            migrationBuilder.DropColumn(
                name: "ExchangeRateSourceReference",
                table: "VendorBills");

            migrationBuilder.DropColumn(
                name: "ExchangeRateToBase",
                table: "VendorBills");

            migrationBuilder.DropColumn(
                name: "TransactionBalanceDue",
                table: "VendorBills");

            migrationBuilder.DropColumn(
                name: "TransactionCurrency",
                table: "VendorBills");

            migrationBuilder.DropColumn(
                name: "TransactionTotalAmount",
                table: "VendorBills");

            migrationBuilder.DropColumn(
                name: "BaseLineTotal",
                table: "VendorBillLines");

            migrationBuilder.DropColumn(
                name: "BaseTaxAmount",
                table: "VendorBillLines");

            migrationBuilder.DropColumn(
                name: "ExchangeRateEffectiveOn",
                table: "SubledgerPayments");

            migrationBuilder.DropColumn(
                name: "ExchangeRateId",
                table: "SubledgerPayments");

            migrationBuilder.DropColumn(
                name: "ExchangeRateSource",
                table: "SubledgerPayments");

            migrationBuilder.DropColumn(
                name: "ExchangeRateSourceReference",
                table: "SubledgerPayments");

            migrationBuilder.DropColumn(
                name: "ExchangeRateToBase",
                table: "SubledgerPayments");

            migrationBuilder.DropColumn(
                name: "RealizedGainLoss",
                table: "SubledgerPayments");

            migrationBuilder.DropColumn(
                name: "TransactionAmount",
                table: "SubledgerPayments");

            migrationBuilder.DropColumn(
                name: "TransactionAppliedAmount",
                table: "SubledgerPayments");

            migrationBuilder.DropColumn(
                name: "TransactionCurrency",
                table: "SubledgerPayments");

            migrationBuilder.DropColumn(
                name: "TransactionUnappliedAmount",
                table: "SubledgerPayments");

            migrationBuilder.DropColumn(
                name: "RealizedGainLoss",
                table: "SubledgerPaymentApplications");

            migrationBuilder.DropColumn(
                name: "TransactionAmount",
                table: "SubledgerPaymentApplications");

            migrationBuilder.DropColumn(
                name: "ExchangeRateEffectiveOn",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "ExchangeRateId",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "ExchangeRateSource",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "ExchangeRateSourceReference",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "ExchangeRateToBase",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "TransactionBalanceDue",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "TransactionCurrency",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "TransactionSubtotal",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "TransactionTaxAmount",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "TransactionTotalAmount",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "BaseLineTotal",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "BaseTaxAmount",
                table: "SalesInvoiceLines");
            */
        }
    }
}
