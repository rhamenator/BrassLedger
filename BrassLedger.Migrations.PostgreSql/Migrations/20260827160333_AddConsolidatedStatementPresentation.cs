using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.PostgreSql.Migrations
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsolidationGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    StatementCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReportingAccountNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReportingAccountName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ReportingAccountType = table.Column<int>(type: "integer", nullable: false),
                    SectionCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SectionName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SectionSortOrder = table.Column<int>(type: "integer", nullable: false),
                    LineCaption = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    LineSortOrder = table.Column<int>(type: "integer", nullable: false),
                    Rationale = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ReviewedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveThrough = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsolidationStatementPresentations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsolidationStatementPresentations_ConsolidationGroups_Con~",
                        column: x => x.ConsolidationGroupId,
                        principalTable: "ConsolidationGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationStatementPresentations_ConsolidationGroupId_S~1",
                table: "ConsolidationStatementPresentations",
                columns: new[] { "ConsolidationGroupId", "StatementCode", "SectionCode", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationStatementPresentations_ConsolidationGroupId_St~",
                table: "ConsolidationStatementPresentations",
                columns: new[] { "ConsolidationGroupId", "StatementCode", "ReportingAccountNumber", "EffectiveFrom" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Downgrade is prohibited because it could delete reviewed statement-presentation policies. Restore a verified pre-upgrade backup instead.");
        }
    }
}
