using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectBillingLineDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProjectCostCodeId",
                table: "ProjectBillingLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectPhaseId",
                table: "ProjectBillingLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectBillingLines_ProjectCostCodeId",
                table: "ProjectBillingLines",
                column: "ProjectCostCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectBillingLines_ProjectPhaseId",
                table: "ProjectBillingLines",
                column: "ProjectPhaseId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectBillingLines_ProjectCostCodes_ProjectCostCodeId",
                table: "ProjectBillingLines",
                column: "ProjectCostCodeId",
                principalTable: "ProjectCostCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectBillingLines_ProjectPhases_ProjectPhaseId",
                table: "ProjectBillingLines",
                column: "ProjectPhaseId",
                principalTable: "ProjectPhases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Downgrade is prohibited because it could delete retained project billing attribution. Restore a verified pre-upgrade backup instead.");
        }
    }
}
