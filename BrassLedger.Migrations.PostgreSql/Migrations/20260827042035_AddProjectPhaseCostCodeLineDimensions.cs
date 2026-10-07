using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectPhaseCostCodeLineDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProjectCostCodeId",
                table: "VendorBillLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectPhaseId",
                table: "VendorBillLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectCostCodeId",
                table: "SalesQuoteLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectPhaseId",
                table: "SalesQuoteLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectCostCodeId",
                table: "SalesOrderLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectPhaseId",
                table: "SalesOrderLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectCostCodeId",
                table: "SalesInvoiceLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectPhaseId",
                table: "SalesInvoiceLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectCostCodeId",
                table: "PurchaseRequisitionLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectPhaseId",
                table: "PurchaseRequisitionLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectCostCodeId",
                table: "PurchaseOrderLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectPhaseId",
                table: "PurchaseOrderLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectCostCodeId",
                table: "PayrollTimeEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectPhaseId",
                table: "PayrollTimeEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectCostCodeId",
                table: "PayrollEarningLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectPhaseId",
                table: "PayrollEarningLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectCostCodeId",
                table: "JournalEntryLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectPhaseId",
                table: "JournalEntryLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VendorBillLines_ProjectCostCodeId",
                table: "VendorBillLines",
                column: "ProjectCostCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorBillLines_ProjectPhaseId",
                table: "VendorBillLines",
                column: "ProjectPhaseId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuoteLines_ProjectCostCodeId",
                table: "SalesQuoteLines",
                column: "ProjectCostCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuoteLines_ProjectPhaseId",
                table: "SalesQuoteLines",
                column: "ProjectPhaseId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_ProjectCostCodeId",
                table: "SalesOrderLines",
                column: "ProjectCostCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_ProjectPhaseId",
                table: "SalesOrderLines",
                column: "ProjectPhaseId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLines_ProjectCostCodeId",
                table: "SalesInvoiceLines",
                column: "ProjectCostCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLines_ProjectPhaseId",
                table: "SalesInvoiceLines",
                column: "ProjectPhaseId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequisitionLines_ProjectCostCodeId",
                table: "PurchaseRequisitionLines",
                column: "ProjectCostCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequisitionLines_ProjectPhaseId",
                table: "PurchaseRequisitionLines",
                column: "ProjectPhaseId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_ProjectCostCodeId",
                table: "PurchaseOrderLines",
                column: "ProjectCostCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_ProjectPhaseId",
                table: "PurchaseOrderLines",
                column: "ProjectPhaseId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollTimeEntries_ProjectCostCodeId",
                table: "PayrollTimeEntries",
                column: "ProjectCostCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollTimeEntries_ProjectPhaseId",
                table: "PayrollTimeEntries",
                column: "ProjectPhaseId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollEarningLines_ProjectCostCodeId",
                table: "PayrollEarningLines",
                column: "ProjectCostCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollEarningLines_ProjectPhaseId",
                table: "PayrollEarningLines",
                column: "ProjectPhaseId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_ProjectCostCodeId",
                table: "JournalEntryLines",
                column: "ProjectCostCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_ProjectPhaseId",
                table: "JournalEntryLines",
                column: "ProjectPhaseId");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_ProjectCostCodes_ProjectCostCodeId",
                table: "JournalEntryLines",
                column: "ProjectCostCodeId",
                principalTable: "ProjectCostCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_ProjectPhases_ProjectPhaseId",
                table: "JournalEntryLines",
                column: "ProjectPhaseId",
                principalTable: "ProjectPhases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollEarningLines_ProjectCostCodes_ProjectCostCodeId",
                table: "PayrollEarningLines",
                column: "ProjectCostCodeId",
                principalTable: "ProjectCostCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollEarningLines_ProjectPhases_ProjectPhaseId",
                table: "PayrollEarningLines",
                column: "ProjectPhaseId",
                principalTable: "ProjectPhases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollTimeEntries_ProjectCostCodes_ProjectCostCodeId",
                table: "PayrollTimeEntries",
                column: "ProjectCostCodeId",
                principalTable: "ProjectCostCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollTimeEntries_ProjectPhases_ProjectPhaseId",
                table: "PayrollTimeEntries",
                column: "ProjectPhaseId",
                principalTable: "ProjectPhases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderLines_ProjectCostCodes_ProjectCostCodeId",
                table: "PurchaseOrderLines",
                column: "ProjectCostCodeId",
                principalTable: "ProjectCostCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderLines_ProjectPhases_ProjectPhaseId",
                table: "PurchaseOrderLines",
                column: "ProjectPhaseId",
                principalTable: "ProjectPhases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseRequisitionLines_ProjectCostCodes_ProjectCostCodeId",
                table: "PurchaseRequisitionLines",
                column: "ProjectCostCodeId",
                principalTable: "ProjectCostCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseRequisitionLines_ProjectPhases_ProjectPhaseId",
                table: "PurchaseRequisitionLines",
                column: "ProjectPhaseId",
                principalTable: "ProjectPhases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceLines_ProjectCostCodes_ProjectCostCodeId",
                table: "SalesInvoiceLines",
                column: "ProjectCostCodeId",
                principalTable: "ProjectCostCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceLines_ProjectPhases_ProjectPhaseId",
                table: "SalesInvoiceLines",
                column: "ProjectPhaseId",
                principalTable: "ProjectPhases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrderLines_ProjectCostCodes_ProjectCostCodeId",
                table: "SalesOrderLines",
                column: "ProjectCostCodeId",
                principalTable: "ProjectCostCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrderLines_ProjectPhases_ProjectPhaseId",
                table: "SalesOrderLines",
                column: "ProjectPhaseId",
                principalTable: "ProjectPhases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesQuoteLines_ProjectCostCodes_ProjectCostCodeId",
                table: "SalesQuoteLines",
                column: "ProjectCostCodeId",
                principalTable: "ProjectCostCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesQuoteLines_ProjectPhases_ProjectPhaseId",
                table: "SalesQuoteLines",
                column: "ProjectPhaseId",
                principalTable: "ProjectPhases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VendorBillLines_ProjectCostCodes_ProjectCostCodeId",
                table: "VendorBillLines",
                column: "ProjectCostCodeId",
                principalTable: "ProjectCostCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VendorBillLines_ProjectPhases_ProjectPhaseId",
                table: "VendorBillLines",
                column: "ProjectPhaseId",
                principalTable: "ProjectPhases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Downgrade is prohibited because it could delete retained project phase and cost-code accounting attribution. Restore a verified pre-upgrade backup instead.");
        }
    }
}
