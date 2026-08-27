using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddConsolidatedStatementPresentation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsolidationStatementPresentations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConsolidationGroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StatementCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ReportingAccountNumber = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ReportingAccountName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    ReportingAccountType = table.Column<int>(type: "INTEGER", nullable: false),
                    SectionCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SectionName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    SectionSortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    LineCaption = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    LineSortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Rationale = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ReviewedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EffectiveThrough = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsolidationStatementPresentations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsolidationStatementPresentations_ConsolidationGroups_ConsolidationGroupId",
                        column: x => x.ConsolidationGroupId,
                        principalTable: "ConsolidationGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationStatementPresentations_ConsolidationGroupId_StatementCode_ReportingAccountNumber_EffectiveFrom",
                table: "ConsolidationStatementPresentations",
                columns: new[] { "ConsolidationGroupId", "StatementCode", "ReportingAccountNumber", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationStatementPresentations_ConsolidationGroupId_StatementCode_SectionCode_EffectiveFrom",
                table: "ConsolidationStatementPresentations",
                columns: new[] { "ConsolidationGroupId", "StatementCode", "SectionCode", "EffectiveFrom" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Downgrade is prohibited because it could delete reviewed statement-presentation policies. Restore a verified pre-upgrade backup instead.");
        }
    }
}
