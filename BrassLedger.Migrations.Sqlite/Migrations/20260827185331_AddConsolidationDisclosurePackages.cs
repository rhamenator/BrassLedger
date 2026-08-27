using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddConsolidationDisclosurePackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsolidationDisclosurePackages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConsolidationGroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    AsOf = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    FrameworkCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    FrameworkEdition = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentJson = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    PreparedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    RejectedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RejectedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DecisionReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ReviewNotes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsolidationDisclosurePackages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsolidationDisclosurePackages_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsolidationDisclosurePackages_ConsolidationGroups_ConsolidationGroupId",
                        column: x => x.ConsolidationGroupId,
                        principalTable: "ConsolidationGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationDisclosurePackages_CompanyId_Status_AsOf",
                table: "ConsolidationDisclosurePackages",
                columns: new[] { "CompanyId", "Status", "AsOf" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationDisclosurePackages_ConsolidationGroupId_PeriodStart_AsOf_FrameworkCode",
                table: "ConsolidationDisclosurePackages",
                columns: new[] { "ConsolidationGroupId", "PeriodStart", "AsOf", "FrameworkCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Downgrade is prohibited because it could delete reviewed consolidated disclosure packages. Restore a verified pre-upgrade backup instead.");
        }
    }
}
