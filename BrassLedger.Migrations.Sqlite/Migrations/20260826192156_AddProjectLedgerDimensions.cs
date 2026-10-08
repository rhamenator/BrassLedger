using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectLedgerDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProjectJobId",
                table: "VendorBillLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectJobId",
                table: "SalesQuoteLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectJobId",
                table: "SalesOrderLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectJobId",
                table: "SalesInvoiceLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectJobId",
                table: "PurchaseRequisitionLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectJobId",
                table: "PurchaseOrderLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingMethod",
                table: "ProjectJobs",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "TimeAndMaterials");

            migrationBuilder.AddColumn<string>(
                name: "CloseReason",
                table: "ProjectJobs",
                type: "TEXT",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosedAtUtc",
                table: "ProjectJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClosedByUserId",
                table: "ProjectJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ClosedOn",
                table: "ProjectJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConcurrencyToken",
                table: "ProjectJobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "legacy-project-v1");

            migrationBuilder.AddColumn<decimal>(
                name: "ContractAmount",
                table: "ProjectJobs",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAtUtc",
                table: "ProjectJobs",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "ProjectJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                table: "ProjectJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpectedEndDate",
                table: "ProjectJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RetainagePercent",
                table: "ProjectJobs",
                type: "TEXT",
                precision: 9,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "StartDate",
                table: "ProjectJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectJobId",
                table: "PayrollEarningLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectJobId",
                table: "JournalEntryLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql("UPDATE ProjectJobs SET Status = 'Active' WHERE Status IN ('Open', 'Billing');");
            migrationBuilder.Sql("UPDATE ProjectJobs SET CustomerId = (SELECT Id FROM Customers WHERE Customers.CompanyId = ProjectJobs.CompanyId AND Customers.Name = ProjectJobs.CustomerName LIMIT 1) WHERE CustomerId IS NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_VendorBillLines_ProjectJobId",
                table: "VendorBillLines",
                column: "ProjectJobId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuoteLines_ProjectJobId",
                table: "SalesQuoteLines",
                column: "ProjectJobId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_ProjectJobId",
                table: "SalesOrderLines",
                column: "ProjectJobId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLines_ProjectJobId",
                table: "SalesInvoiceLines",
                column: "ProjectJobId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequisitionLines_ProjectJobId",
                table: "PurchaseRequisitionLines",
                column: "ProjectJobId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_ProjectJobId",
                table: "PurchaseOrderLines",
                column: "ProjectJobId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectJobs_CustomerId",
                table: "ProjectJobs",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollEarningLines_ProjectJobId",
                table: "PayrollEarningLines",
                column: "ProjectJobId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_ProjectJobId_JournalEntryId",
                table: "JournalEntryLines",
                columns: new[] { "ProjectJobId", "JournalEntryId" });

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_ProjectJobs_ProjectJobId",
                table: "JournalEntryLines",
                column: "ProjectJobId",
                principalTable: "ProjectJobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollEarningLines_ProjectJobs_ProjectJobId",
                table: "PayrollEarningLines",
                column: "ProjectJobId",
                principalTable: "ProjectJobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectJobs_Customers_CustomerId",
                table: "ProjectJobs",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderLines_ProjectJobs_ProjectJobId",
                table: "PurchaseOrderLines",
                column: "ProjectJobId",
                principalTable: "ProjectJobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseRequisitionLines_ProjectJobs_ProjectJobId",
                table: "PurchaseRequisitionLines",
                column: "ProjectJobId",
                principalTable: "ProjectJobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceLines_ProjectJobs_ProjectJobId",
                table: "SalesInvoiceLines",
                column: "ProjectJobId",
                principalTable: "ProjectJobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrderLines_ProjectJobs_ProjectJobId",
                table: "SalesOrderLines",
                column: "ProjectJobId",
                principalTable: "ProjectJobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesQuoteLines_ProjectJobs_ProjectJobId",
                table: "SalesQuoteLines",
                column: "ProjectJobId",
                principalTable: "ProjectJobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VendorBillLines_ProjectJobs_ProjectJobId",
                table: "VendorBillLines",
                column: "ProjectJobId",
                principalTable: "ProjectJobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RejectProjectDimensionDowngrade();
            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntryLines_ProjectJobs_ProjectJobId",
                table: "JournalEntryLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PayrollEarningLines_ProjectJobs_ProjectJobId",
                table: "PayrollEarningLines");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectJobs_Customers_CustomerId",
                table: "ProjectJobs");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrderLines_ProjectJobs_ProjectJobId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseRequisitionLines_ProjectJobs_ProjectJobId",
                table: "PurchaseRequisitionLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceLines_ProjectJobs_ProjectJobId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrderLines_ProjectJobs_ProjectJobId",
                table: "SalesOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesQuoteLines_ProjectJobs_ProjectJobId",
                table: "SalesQuoteLines");

            migrationBuilder.DropForeignKey(
                name: "FK_VendorBillLines_ProjectJobs_ProjectJobId",
                table: "VendorBillLines");

            migrationBuilder.DropIndex(
                name: "IX_VendorBillLines_ProjectJobId",
                table: "VendorBillLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesQuoteLines_ProjectJobId",
                table: "SalesQuoteLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrderLines_ProjectJobId",
                table: "SalesOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceLines_ProjectJobId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseRequisitionLines_ProjectJobId",
                table: "PurchaseRequisitionLines");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrderLines_ProjectJobId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_ProjectJobs_CustomerId",
                table: "ProjectJobs");

            migrationBuilder.DropIndex(
                name: "IX_PayrollEarningLines_ProjectJobId",
                table: "PayrollEarningLines");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntryLines_ProjectJobId_JournalEntryId",
                table: "JournalEntryLines");

            migrationBuilder.DropColumn(
                name: "ProjectJobId",
                table: "VendorBillLines");

            migrationBuilder.DropColumn(
                name: "ProjectJobId",
                table: "SalesQuoteLines");

            migrationBuilder.DropColumn(
                name: "ProjectJobId",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "ProjectJobId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "ProjectJobId",
                table: "PurchaseRequisitionLines");

            migrationBuilder.DropColumn(
                name: "ProjectJobId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "BillingMethod",
                table: "ProjectJobs");

            migrationBuilder.DropColumn(
                name: "CloseReason",
                table: "ProjectJobs");

            migrationBuilder.DropColumn(
                name: "ClosedAtUtc",
                table: "ProjectJobs");

            migrationBuilder.DropColumn(
                name: "ClosedByUserId",
                table: "ProjectJobs");

            migrationBuilder.DropColumn(
                name: "ClosedOn",
                table: "ProjectJobs");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "ProjectJobs");

            migrationBuilder.DropColumn(
                name: "ContractAmount",
                table: "ProjectJobs");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "ProjectJobs");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "ProjectJobs");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "ProjectJobs");

            migrationBuilder.DropColumn(
                name: "ExpectedEndDate",
                table: "ProjectJobs");

            migrationBuilder.DropColumn(
                name: "RetainagePercent",
                table: "ProjectJobs");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "ProjectJobs");

            migrationBuilder.DropColumn(
                name: "ProjectJobId",
                table: "PayrollEarningLines");

            migrationBuilder.DropColumn(
                name: "ProjectJobId",
                table: "JournalEntryLines");
        }

        private static void RejectProjectDimensionDowngrade() =>
            throw new NotSupportedException("Downgrade is prohibited because it could delete project attribution and project lifecycle evidence. Restore a verified pre-upgrade backup instead.");
    }
}
