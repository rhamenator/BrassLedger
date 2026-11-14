using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryReceiptCurrencyProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExchangeRateDirection",
                table: "InventoryReceipts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExchangeRateEffectiveOn",
                table: "InventoryReceipts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExchangeRateId",
                table: "InventoryReceipts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExchangeRateSource",
                table: "InventoryReceipts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExchangeRateSourceReference",
                table: "InventoryReceipts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRateToBase",
                table: "InventoryReceipts",
                type: "numeric(18,10)",
                precision: 18,
                scale: 10,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionAmount",
                table: "InventoryReceipts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "TransactionCurrency",
                table: "InventoryReceipts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionAmount",
                table: "InventoryReceiptLines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE "InventoryReceipts" AS receipt
                SET "TransactionCurrency" = company."BaseCurrency",
                    "TransactionAmount" = receipt."TotalAmount",
                    "ExchangeRateToBase" = 1,
                    "ExchangeRateEffectiveOn" = receipt."ReceivedOn",
                    "ExchangeRateSource" = 'Company base currency',
                    "ExchangeRateDirection" = 'Base'
                FROM "Companies" AS company
                WHERE company."Id" = receipt."CompanyId";
                UPDATE "InventoryReceiptLines" SET "TransactionAmount" = "LineTotal";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Downgrade prohibited because it could delete receipt currency and valuation provenance. Restore a verified pre-upgrade backup instead.");
        }
    }
}
