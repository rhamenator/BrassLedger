using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackingDimensionsToSourceLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClassId",
                table: "VendorBillLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "VendorBillLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClassId",
                table: "SalesQuoteLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "SalesQuoteLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClassId",
                table: "SalesOrderLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "SalesOrderLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClassId",
                table: "SalesInvoiceLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "SalesInvoiceLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClassId",
                table: "PurchaseRequisitionLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "PurchaseRequisitionLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClassId",
                table: "PurchaseOrderLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "PurchaseOrderLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClassId",
                table: "ProjectBillingLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "ProjectBillingLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClassId",
                table: "PayrollTimeEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "PayrollTimeEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClassId",
                table: "PayrollEarningLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "PayrollEarningLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VendorBillLines_ClassId",
                table: "VendorBillLines",
                column: "ClassId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorBillLines_DepartmentId",
                table: "VendorBillLines",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuoteLines_ClassId",
                table: "SalesQuoteLines",
                column: "ClassId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuoteLines_DepartmentId",
                table: "SalesQuoteLines",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_ClassId",
                table: "SalesOrderLines",
                column: "ClassId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_DepartmentId",
                table: "SalesOrderLines",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLines_ClassId",
                table: "SalesInvoiceLines",
                column: "ClassId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLines_DepartmentId",
                table: "SalesInvoiceLines",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequisitionLines_ClassId",
                table: "PurchaseRequisitionLines",
                column: "ClassId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequisitionLines_DepartmentId",
                table: "PurchaseRequisitionLines",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_ClassId",
                table: "PurchaseOrderLines",
                column: "ClassId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_DepartmentId",
                table: "PurchaseOrderLines",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectBillingLines_ClassId",
                table: "ProjectBillingLines",
                column: "ClassId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectBillingLines_DepartmentId",
                table: "ProjectBillingLines",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollTimeEntries_ClassId",
                table: "PayrollTimeEntries",
                column: "ClassId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollTimeEntries_DepartmentId",
                table: "PayrollTimeEntries",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollEarningLines_ClassId",
                table: "PayrollEarningLines",
                column: "ClassId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollEarningLines_DepartmentId",
                table: "PayrollEarningLines",
                column: "DepartmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollEarningLines_TrackingDimensionValues_ClassId",
                table: "PayrollEarningLines",
                column: "ClassId",
                principalTable: "TrackingDimensionValues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollEarningLines_TrackingDimensionValues_DepartmentId",
                table: "PayrollEarningLines",
                column: "DepartmentId",
                principalTable: "TrackingDimensionValues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollTimeEntries_TrackingDimensionValues_ClassId",
                table: "PayrollTimeEntries",
                column: "ClassId",
                principalTable: "TrackingDimensionValues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollTimeEntries_TrackingDimensionValues_DepartmentId",
                table: "PayrollTimeEntries",
                column: "DepartmentId",
                principalTable: "TrackingDimensionValues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectBillingLines_TrackingDimensionValues_ClassId",
                table: "ProjectBillingLines",
                column: "ClassId",
                principalTable: "TrackingDimensionValues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectBillingLines_TrackingDimensionValues_DepartmentId",
                table: "ProjectBillingLines",
                column: "DepartmentId",
                principalTable: "TrackingDimensionValues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderLines_TrackingDimensionValues_ClassId",
                table: "PurchaseOrderLines",
                column: "ClassId",
                principalTable: "TrackingDimensionValues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderLines_TrackingDimensionValues_DepartmentId",
                table: "PurchaseOrderLines",
                column: "DepartmentId",
                principalTable: "TrackingDimensionValues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseRequisitionLines_TrackingDimensionValues_ClassId",
                table: "PurchaseRequisitionLines",
                column: "ClassId",
                principalTable: "TrackingDimensionValues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseRequisitionLines_TrackingDimensionValues_Department~",
                table: "PurchaseRequisitionLines",
                column: "DepartmentId",
                principalTable: "TrackingDimensionValues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceLines_TrackingDimensionValues_ClassId",
                table: "SalesInvoiceLines",
                column: "ClassId",
                principalTable: "TrackingDimensionValues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceLines_TrackingDimensionValues_DepartmentId",
                table: "SalesInvoiceLines",
                column: "DepartmentId",
                principalTable: "TrackingDimensionValues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrderLines_TrackingDimensionValues_ClassId",
                table: "SalesOrderLines",
                column: "ClassId",
                principalTable: "TrackingDimensionValues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrderLines_TrackingDimensionValues_DepartmentId",
                table: "SalesOrderLines",
                column: "DepartmentId",
                principalTable: "TrackingDimensionValues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesQuoteLines_TrackingDimensionValues_ClassId",
                table: "SalesQuoteLines",
                column: "ClassId",
                principalTable: "TrackingDimensionValues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesQuoteLines_TrackingDimensionValues_DepartmentId",
                table: "SalesQuoteLines",
                column: "DepartmentId",
                principalTable: "TrackingDimensionValues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VendorBillLines_TrackingDimensionValues_ClassId",
                table: "VendorBillLines",
                column: "ClassId",
                principalTable: "TrackingDimensionValues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VendorBillLines_TrackingDimensionValues_DepartmentId",
                table: "VendorBillLines",
                column: "DepartmentId",
                principalTable: "TrackingDimensionValues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Rolling back tracking dimensions from source lines could delete accounting classifications and is prohibited. Restore a verified backup instead.");
        }
    }
}
