using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddConsolidationAccountMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsolidationAccountMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConsolidationGroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MemberCompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MemberAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReportingAccountNumber = table.Column<string>(type: "TEXT", nullable: false),
                    ReportingAccountName = table.Column<string>(type: "TEXT", nullable: false),
                    ReportingAccountType = table.Column<int>(type: "INTEGER", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EffectiveThrough = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsolidationAccountMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsolidationAccountMappings_Accounts_MemberAccountId",
                        column: x => x.MemberAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsolidationAccountMappings_Companies_MemberCompanyId",
                        column: x => x.MemberCompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsolidationAccountMappings_ConsolidationGroups_ConsolidationGroupId",
                        column: x => x.ConsolidationGroupId,
                        principalTable: "ConsolidationGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationAccountMappings_ConsolidationGroupId_MemberCompanyId_MemberAccountId_EffectiveFrom",
                table: "ConsolidationAccountMappings",
                columns: new[] { "ConsolidationGroupId", "MemberCompanyId", "MemberAccountId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationAccountMappings_ConsolidationGroupId_ReportingAccountNumber",
                table: "ConsolidationAccountMappings",
                columns: new[] { "ConsolidationGroupId", "ReportingAccountNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationAccountMappings_MemberAccountId",
                table: "ConsolidationAccountMappings",
                column: "MemberAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationAccountMappings_MemberCompanyId",
                table: "ConsolidationAccountMappings",
                column: "MemberCompanyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Rolling back consolidation account mappings could delete effective-dated reporting classifications and is prohibited. Restore a verified backup instead.");
        }
    }
}
