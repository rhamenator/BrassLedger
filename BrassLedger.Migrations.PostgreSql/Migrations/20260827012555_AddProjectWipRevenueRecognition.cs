using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectWipRevenueRecognition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RevenueRecognitionMethod",
                table: "ProjectJobs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "AsBilled");

            migrationBuilder.CreateTable(
                name: "ProjectWipSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    ThroughDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PostingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RecognitionMethod = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ManualCompletionPercent = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    ContractAmountSnapshot = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    EstimatedCostSnapshot = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ActualCostToDate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CompletionPercent = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    EarnedRevenueToDate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BilledRevenueToDate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PriorContractAsset = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PriorContractLiability = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DesiredContractAsset = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DesiredContractLiability = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RevenueAdjustment = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RevenueAccountNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PreviewFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PreparedProjectConcurrencyToken = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PreparedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RejectedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RejectedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecisionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PostedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    PostedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    JournalEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversalJournalEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReversalDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ReversalReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CancelledByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectWipSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectWipSchedules_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectWipSchedules_JournalEntries_ReversalJournalEntryId",
                        column: x => x.ReversalJournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectWipSchedules_ProjectJobs_ProjectJobId",
                        column: x => x.ProjectJobId,
                        principalTable: "ProjectJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectWipSchedules_CompanyId_ProjectJobId_ThroughDate_Stat~",
                table: "ProjectWipSchedules",
                columns: new[] { "CompanyId", "ProjectJobId", "ThroughDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectWipSchedules_JournalEntryId",
                table: "ProjectWipSchedules",
                column: "JournalEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectWipSchedules_ProjectJobId",
                table: "ProjectWipSchedules",
                column: "ProjectJobId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectWipSchedules_ReversalJournalEntryId",
                table: "ProjectWipSchedules",
                column: "ReversalJournalEntryId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Downgrade is prohibited because it could delete project WIP calculations, review decisions, control positions, and journal provenance. Restore a verified pre-upgrade backup instead.");
        }
    }
}
