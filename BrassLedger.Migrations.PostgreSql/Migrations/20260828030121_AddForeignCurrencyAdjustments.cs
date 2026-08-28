using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddForeignCurrencyAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CarryingAmount",
                table: "SubledgerAdjustments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExchangeRateEffectiveOn",
                table: "SubledgerAdjustments",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExchangeRateId",
                table: "SubledgerAdjustments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExchangeRateSource",
                table: "SubledgerAdjustments",
                type: "character varying(240)",
                maxLength: 240,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExchangeRateSourceReference",
                table: "SubledgerAdjustments",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRateToBase",
                table: "SubledgerAdjustments",
                type: "numeric(18,10)",
                precision: 18,
                scale: 10,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "RateBasis",
                table: "SubledgerAdjustments",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "RealizedGainLoss",
                table: "SubledgerAdjustments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ReversalDate",
                table: "SubledgerAdjustments",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionAmount",
                table: "SubledgerAdjustments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "TransactionCurrency",
                table: "SubledgerAdjustments",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE "SubledgerAdjustments"
                SET "TransactionCurrency" = COALESCE((SELECT "BaseCurrency" FROM "Companies" WHERE "Companies"."Id" = "SubledgerAdjustments"."CompanyId"), 'USD'),
                    "TransactionAmount" = "Amount", "CarryingAmount" = "Amount", "RateBasis" = 'BaseCurrency', "ExchangeRateToBase" = 1,
                    "ExchangeRateEffectiveOn" = "AdjustmentDate", "ExchangeRateSource" = 'Legacy base-currency adjustment';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SubledgerAdjustments_ExchangeRateId",
                table: "SubledgerAdjustments",
                column: "ExchangeRateId");

            migrationBuilder.AddForeignKey(
                name: "FK_SubledgerAdjustments_CurrencyExchangeRates_ExchangeRateId",
                table: "SubledgerAdjustments",
                column: "ExchangeRateId",
                principalTable: "CurrencyExchangeRates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Downgrade is prohibited because it could delete retained transaction-currency adjustment amounts, carrying values, exchange-rate provenance, realized foreign-exchange results, and reversal dates. Restore a verified pre-upgrade backup instead.");
            /* The generated destructive downgrade is retained below for schema-review traceability only.
            migrationBuilder.DropForeignKey(
                name: "FK_SubledgerAdjustments_CurrencyExchangeRates_ExchangeRateId",
                table: "SubledgerAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_SubledgerAdjustments_ExchangeRateId",
                table: "SubledgerAdjustments");

            migrationBuilder.DropColumn(
                name: "CarryingAmount",
                table: "SubledgerAdjustments");

            migrationBuilder.DropColumn(
                name: "ExchangeRateEffectiveOn",
                table: "SubledgerAdjustments");

            migrationBuilder.DropColumn(
                name: "ExchangeRateId",
                table: "SubledgerAdjustments");

            migrationBuilder.DropColumn(
                name: "ExchangeRateSource",
                table: "SubledgerAdjustments");

            migrationBuilder.DropColumn(
                name: "ExchangeRateSourceReference",
                table: "SubledgerAdjustments");

            migrationBuilder.DropColumn(
                name: "ExchangeRateToBase",
                table: "SubledgerAdjustments");

            migrationBuilder.DropColumn(
                name: "RateBasis",
                table: "SubledgerAdjustments");

            migrationBuilder.DropColumn(
                name: "RealizedGainLoss",
                table: "SubledgerAdjustments");

            migrationBuilder.DropColumn(
                name: "ReversalDate",
                table: "SubledgerAdjustments");

            migrationBuilder.DropColumn(
                name: "TransactionAmount",
                table: "SubledgerAdjustments");

            migrationBuilder.DropColumn(
                name: "TransactionCurrency",
                table: "SubledgerAdjustments");
            */
        }
    }
}
