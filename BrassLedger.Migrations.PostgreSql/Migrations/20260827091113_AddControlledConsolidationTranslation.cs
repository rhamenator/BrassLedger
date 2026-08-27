using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddControlledConsolidationTranslation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CurrencyExchangeRates_CompanyId_BaseCurrency_QuoteCurrency_~",
                table: "CurrencyExchangeRates");

            migrationBuilder.AddColumn<string>(
                name: "ConcurrencyToken",
                table: "CurrencyExchangeRates",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "CurrencyExchangeRates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PeriodStartOn",
                table: "CurrencyExchangeRates",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RateType",
                table: "CurrencyExchangeRates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RetrievedOn",
                table: "CurrencyExchangeRates",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceReference",
                table: "CurrencyExchangeRates",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CtaAccountName",
                table: "ConsolidationGroups",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CtaAccountNumber",
                table: "ConsolidationGroups",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "TranslationMethod",
                table: "ConsolidationAccountMappings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                UPDATE "CurrencyExchangeRates"
                SET "IsActive" = TRUE,
                    "ConcurrencyToken" = md5(random()::text || clock_timestamp()::text || "Id"::text);

                UPDATE "ConsolidationAccountMappings"
                SET "TranslationMethod" = CASE
                    WHEN "ReportingAccountType" IN (3, 4) THEN 1
                    WHEN "ReportingAccountType" = 2 THEN 2
                    ELSE 0
                END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_CurrencyExchangeRates_TypedEffective",
                table: "CurrencyExchangeRates",
                columns: new[] { "CompanyId", "BaseCurrency", "QuoteCurrency", "RateType", "EffectiveOn" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Downgrade is prohibited because it could delete retained rate types, source provenance, mapping translation policies, and CTA configuration.");
        }
    }
}
