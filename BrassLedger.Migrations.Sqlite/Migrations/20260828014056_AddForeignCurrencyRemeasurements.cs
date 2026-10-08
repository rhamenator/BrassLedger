using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddForeignCurrencyRemeasurements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ForeignCurrencyRemeasurementBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AsOf = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    NetAdjustment = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReversalJournalEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PreparedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    RejectedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RejectedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PostedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PostedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReversedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReversedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReversalDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    DecisionReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ReversalReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForeignCurrencyRemeasurementBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ForeignCurrencyRemeasurementBatches_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ForeignCurrencyRemeasurementBatches_JournalEntries_ReversalJournalEntryId",
                        column: x => x.ReversalJournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ForeignCurrencyRemeasurementLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ForeignCurrencyRemeasurementBatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentType = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CounterpartyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TransactionCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    TransactionBalance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    PreviousBaseBalance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    RemeasuredBaseBalance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    AdjustmentAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ExchangeRateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExchangeRateToBase = table.Column<decimal>(type: "TEXT", precision: 18, scale: 10, nullable: false),
                    ExchangeRateEffectiveOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ExchangeRateSource = table.Column<string>(type: "TEXT", maxLength: 240, nullable: false),
                    ExchangeRateSourceReference = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForeignCurrencyRemeasurementLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ForeignCurrencyRemeasurementLines_CurrencyExchangeRates_ExchangeRateId",
                        column: x => x.ExchangeRateId,
                        principalTable: "CurrencyExchangeRates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ForeignCurrencyRemeasurementLines_ForeignCurrencyRemeasurementBatches_ForeignCurrencyRemeasurementBatchId",
                        column: x => x.ForeignCurrencyRemeasurementBatchId,
                        principalTable: "ForeignCurrencyRemeasurementBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ForeignCurrencyRemeasurementBatches_CompanyId_AsOf",
                table: "ForeignCurrencyRemeasurementBatches",
                columns: new[] { "CompanyId", "AsOf" },
                unique: true,
                filter: "\"Status\" IN ('Draft', 'Approved', 'Posted')");

            migrationBuilder.CreateIndex(
                name: "IX_ForeignCurrencyRemeasurementBatches_CompanyId_Reference",
                table: "ForeignCurrencyRemeasurementBatches",
                columns: new[] { "CompanyId", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ForeignCurrencyRemeasurementBatches_JournalEntryId",
                table: "ForeignCurrencyRemeasurementBatches",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_ForeignCurrencyRemeasurementBatches_ReversalJournalEntryId",
                table: "ForeignCurrencyRemeasurementBatches",
                column: "ReversalJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_ForeignCurrencyRemeasurementLines_ExchangeRateId",
                table: "ForeignCurrencyRemeasurementLines",
                column: "ExchangeRateId");

            migrationBuilder.CreateIndex(
                name: "IX_ForeignCurrencyRemeasurementLines_ForeignCurrencyRemeasurementBatchId_DocumentType_DocumentId",
                table: "ForeignCurrencyRemeasurementLines",
                columns: new[] { "ForeignCurrencyRemeasurementBatchId", "DocumentType", "DocumentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Downgrade is prohibited because it could delete retained remeasurement calculations, reviews, rate provenance, reversal dates, and journal links. Restore a verified pre-upgrade backup instead.");
        }
    }
}
