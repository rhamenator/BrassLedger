using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddEffectiveDatedConsolidationOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ConsolidationGroupCompanies_ConsolidationGroupId_MemberCompanyId",
                table: "ConsolidationGroupCompanies");

            migrationBuilder.AddColumn<string>(
                name: "ConcurrencyToken",
                table: "ConsolidationGroups",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ConcurrencyToken",
                table: "ConsolidationGroupCompanies",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "EffectiveFrom",
                table: "ConsolidationGroupCompanies",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<DateOnly>(
                name: "EffectiveThrough",
                table: "ConsolidationGroupCompanies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql("UPDATE ConsolidationGroups SET ConcurrencyToken = lower(hex(randomblob(16))) WHERE ConcurrencyToken = '';");
            migrationBuilder.Sql("UPDATE ConsolidationGroupCompanies SET ConcurrencyToken = lower(hex(randomblob(16))) WHERE ConcurrencyToken = '';");

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationGroupCompanies_ConsolidationGroupId_MemberCompanyId_EffectiveFrom",
                table: "ConsolidationGroupCompanies",
                columns: new[] { "ConsolidationGroupId", "MemberCompanyId", "EffectiveFrom" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Rolling back effective-dated consolidation ownership could delete ownership periods and audit concurrency evidence and is prohibited. Restore a verified backup instead.");
        }
    }
}
