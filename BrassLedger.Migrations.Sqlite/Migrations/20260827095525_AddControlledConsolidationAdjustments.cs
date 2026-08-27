using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddControlledConsolidationAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsolidationAdjustmentBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConsolidationGroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    AsOf = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    MatchReference = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    PreparedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    RejectedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RejectedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PostedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PostedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DecisionReason = table.Column<string>(type: "TEXT", nullable: false),
                    ReversalOfBatchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReversedByBatchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReversalReason = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsolidationAdjustmentBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsolidationAdjustmentBatches_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsolidationAdjustmentBatches_ConsolidationAdjustmentBatches_ReversalOfBatchId",
                        column: x => x.ReversalOfBatchId,
                        principalTable: "ConsolidationAdjustmentBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsolidationAdjustmentBatches_ConsolidationGroups_ConsolidationGroupId",
                        column: x => x.ConsolidationGroupId,
                        principalTable: "ConsolidationGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConsolidationAdjustmentLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConsolidationAdjustmentBatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    ReportingAccountNumber = table.Column<string>(type: "TEXT", nullable: false),
                    ReportingAccountName = table.Column<string>(type: "TEXT", nullable: false),
                    ReportingAccountType = table.Column<int>(type: "INTEGER", nullable: false),
                    Debit = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Credit = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    SourceCompanyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CounterpartyCompanyId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsolidationAdjustmentLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsolidationAdjustmentLines_Companies_CounterpartyCompanyId",
                        column: x => x.CounterpartyCompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsolidationAdjustmentLines_Companies_SourceCompanyId",
                        column: x => x.SourceCompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsolidationAdjustmentLines_ConsolidationAdjustmentBatches_ConsolidationAdjustmentBatchId",
                        column: x => x.ConsolidationAdjustmentBatchId,
                        principalTable: "ConsolidationAdjustmentBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationAdjustmentBatches_CompanyId",
                table: "ConsolidationAdjustmentBatches",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationAdjustmentBatches_ConsolidationGroupId_PeriodStart_AsOf_Reference",
                table: "ConsolidationAdjustmentBatches",
                columns: new[] { "ConsolidationGroupId", "PeriodStart", "AsOf", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationAdjustmentBatches_ReversalOfBatchId",
                table: "ConsolidationAdjustmentBatches",
                column: "ReversalOfBatchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationAdjustmentLines_ConsolidationAdjustmentBatchId_Sequence",
                table: "ConsolidationAdjustmentLines",
                columns: new[] { "ConsolidationAdjustmentBatchId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationAdjustmentLines_CounterpartyCompanyId",
                table: "ConsolidationAdjustmentLines",
                column: "CounterpartyCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationAdjustmentLines_SourceCompanyId",
                table: "ConsolidationAdjustmentLines",
                column: "SourceCompanyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Downgrade is prohibited because it could delete retained consolidation adjustments, intercompany eliminations, review decisions, reversals, and audit provenance. Restore a verified pre-upgrade backup instead.");
        }
    }
}
