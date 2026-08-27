using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddEffectiveDatedConsolidationOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ConsolidationGroupCompanies_ConsolidationGroupId_MemberComp~",
                table: "ConsolidationGroupCompanies");

            migrationBuilder.AddColumn<string>(
                name: "ConcurrencyToken",
                table: "ConsolidationGroups",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ConcurrencyToken",
                table: "ConsolidationGroupCompanies",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "EffectiveFrom",
                table: "ConsolidationGroupCompanies",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<DateOnly>(
                name: "EffectiveThrough",
                table: "ConsolidationGroupCompanies",
                type: "date",
                nullable: true);

            migrationBuilder.Sql("UPDATE \"ConsolidationGroups\" SET \"ConcurrencyToken\" = md5(random()::text || clock_timestamp()::text || \"Id\"::text) WHERE \"ConcurrencyToken\" = '';");
            migrationBuilder.Sql("UPDATE \"ConsolidationGroupCompanies\" SET \"ConcurrencyToken\" = md5(random()::text || clock_timestamp()::text || \"Id\"::text) WHERE \"ConcurrencyToken\" = '';");

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationGroupCompanies_ConsolidationGroupId_MemberComp~",
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
