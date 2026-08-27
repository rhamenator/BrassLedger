using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddExplicitConsolidationBasisAndNci : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NciAccountName",
                table: "ConsolidationGroups",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NciAccountNumber",
                table: "ConsolidationGroups",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BasisRationale",
                table: "ConsolidationGroupCompanies",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "BasisReviewedOn",
                table: "ConsolidationGroupCompanies",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConsolidationBasis",
                table: "ConsolidationGroupCompanies",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<string>(
                name: "ControlKey",
                table: "ConsolidationAdjustmentBatches",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubjectCompanyId",
                table: "ConsolidationAdjustmentBatches",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationAdjustmentBatches_ControlKey",
                table: "ConsolidationAdjustmentBatches",
                column: "ControlKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationAdjustmentBatches_SubjectCompanyId",
                table: "ConsolidationAdjustmentBatches",
                column: "SubjectCompanyId");

            migrationBuilder.AddForeignKey(
                name: "FK_ConsolidationAdjustmentBatches_Companies_SubjectCompanyId",
                table: "ConsolidationAdjustmentBatches",
                column: "SubjectCompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Downgrade is prohibited because it could delete reviewed consolidation-basis evidence, NCI reclassifications, controlled subject provenance, and concurrency identities. Restore a verified pre-upgrade backup instead.");
        }
    }
}
