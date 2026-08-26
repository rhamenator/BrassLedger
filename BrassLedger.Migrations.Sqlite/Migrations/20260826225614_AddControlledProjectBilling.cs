using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddControlledProjectBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectBillingProposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectJobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubledgerDocumentWorkflowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RetainageReleaseOfProposalId = table.Column<Guid>(type: "TEXT", nullable: true),
                    InvoiceNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    BillingThrough = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    InvoiceDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    BillingBasis = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ProgressPercentToDate = table.Column<decimal>(type: "TEXT", precision: 9, scale: 6, nullable: false),
                    CostMarkupPercent = table.Column<decimal>(type: "TEXT", precision: 9, scale: 6, nullable: false),
                    ContractAmountSnapshot = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    RetainagePercentSnapshot = table.Column<decimal>(type: "TEXT", precision: 9, scale: 6, nullable: false),
                    GrossAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    RetainageAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    InvoiceAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    RevenueAccountNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    PreviewFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PreparedProjectConcurrencyToken = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PreparedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CancelledByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CancellationReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectBillingProposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectBillingProposals_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectBillingProposals_ProjectBillingProposals_RetainageReleaseOfProposalId",
                        column: x => x.RetainageReleaseOfProposalId,
                        principalTable: "ProjectBillingProposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectBillingProposals_ProjectJobs_ProjectJobId",
                        column: x => x.ProjectJobId,
                        principalTable: "ProjectJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectBillingProposals_SubledgerDocumentWorkflows_SubledgerDocumentWorkflowId",
                        column: x => x.SubledgerDocumentWorkflowId,
                        principalTable: "SubledgerDocumentWorkflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectBillingRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectJobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EarningCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    HourlyRate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    EffectiveOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EffectiveThrough = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectBillingRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectBillingRates_ProjectJobs_ProjectJobId",
                        column: x => x.ProjectJobId,
                        principalTable: "ProjectJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectBillingLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectBillingProposalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    SourceCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    MarkupAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    GrossAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    RetainageAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    InvoiceAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    RevenueAccountNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectBillingLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectBillingLines_ProjectBillingProposals_ProjectBillingProposalId",
                        column: x => x.ProjectBillingProposalId,
                        principalTable: "ProjectBillingProposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectBillingSourceReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectJobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ProjectBillingProposalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectBillingSourceReservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectBillingSourceReservations_ProjectBillingProposals_ProjectBillingProposalId",
                        column: x => x.ProjectBillingProposalId,
                        principalTable: "ProjectBillingProposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectBillingSourceReservations_ProjectJobs_ProjectJobId",
                        column: x => x.ProjectJobId,
                        principalTable: "ProjectJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectBillingLines_ProjectBillingProposalId_Sequence",
                table: "ProjectBillingLines",
                columns: new[] { "ProjectBillingProposalId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectBillingProposals_CompanyId_InvoiceNumber",
                table: "ProjectBillingProposals",
                columns: new[] { "CompanyId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectBillingProposals_CompanyId_ProjectJobId_Status_BillingThrough",
                table: "ProjectBillingProposals",
                columns: new[] { "CompanyId", "ProjectJobId", "Status", "BillingThrough" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectBillingProposals_CustomerId",
                table: "ProjectBillingProposals",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectBillingProposals_ProjectJobId",
                table: "ProjectBillingProposals",
                column: "ProjectJobId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectBillingProposals_RetainageReleaseOfProposalId",
                table: "ProjectBillingProposals",
                column: "RetainageReleaseOfProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectBillingProposals_SubledgerDocumentWorkflowId",
                table: "ProjectBillingProposals",
                column: "SubledgerDocumentWorkflowId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectBillingRates_CompanyId_ProjectJobId_EarningCode_EffectiveOn",
                table: "ProjectBillingRates",
                columns: new[] { "CompanyId", "ProjectJobId", "EarningCode", "EffectiveOn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectBillingRates_ProjectJobId",
                table: "ProjectBillingRates",
                column: "ProjectJobId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectBillingSourceReservations_CompanyId_SourceKey",
                table: "ProjectBillingSourceReservations",
                columns: new[] { "CompanyId", "SourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectBillingSourceReservations_ProjectBillingProposalId",
                table: "ProjectBillingSourceReservations",
                column: "ProjectBillingProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectBillingSourceReservations_ProjectJobId",
                table: "ProjectBillingSourceReservations",
                column: "ProjectJobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Downgrade is prohibited because it could delete project billing derivation, retainage, rate, source-reservation, and invoice-workflow history. Restore a verified pre-upgrade backup instead.");
        }
    }
}
