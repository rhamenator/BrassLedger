using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesQuotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SalesQuoteId",
                table: "SalesOrders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SalesQuotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuoteNumber = table.Column<string>(type: "TEXT", nullable: false),
                    QuotedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    PreparedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    WithdrawnByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    WithdrawnAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    WithdrawalReason = table.Column<string>(type: "TEXT", nullable: false),
                    ConvertedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConvertedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesQuotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesQuotes_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesQuoteLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SalesQuoteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RevenueAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesQuoteLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesQuoteLines_Accounts_RevenueAccountId",
                        column: x => x.RevenueAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesQuoteLines_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesQuoteLines_SalesQuotes_SalesQuoteId",
                        column: x => x.SalesQuoteId,
                        principalTable: "SalesQuotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_SalesQuoteId",
                table: "SalesOrders",
                column: "SalesQuoteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuoteLines_InventoryItemId",
                table: "SalesQuoteLines",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuoteLines_RevenueAccountId",
                table: "SalesQuoteLines",
                column: "RevenueAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuoteLines_SalesQuoteId_Sequence",
                table: "SalesQuoteLines",
                columns: new[] { "SalesQuoteId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuotes_CompanyId_QuoteNumber",
                table: "SalesQuotes",
                columns: new[] { "CompanyId", "QuoteNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuotes_CustomerId",
                table: "SalesQuotes",
                column: "CustomerId");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrders_SalesQuotes_SalesQuoteId",
                table: "SalesOrders",
                column: "SalesQuoteId",
                principalTable: "SalesQuotes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider is not null)
                throw new NotSupportedException("Rolling back sales quotes could delete commercial terms, conversion provenance, and audit-relevant history and is prohibited. Restore a verified pre-upgrade backup instead.");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrders_SalesQuotes_SalesQuoteId",
                table: "SalesOrders");

            migrationBuilder.DropTable(
                name: "SalesQuoteLines");

            migrationBuilder.DropTable(
                name: "SalesQuotes");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrders_SalesQuoteId",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "SalesQuoteId",
                table: "SalesOrders");
        }
    }
}
