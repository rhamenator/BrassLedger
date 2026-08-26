using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class ScopeVendorBillNumbersByVendor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VendorBills_CompanyId_BillNumber",
                table: "VendorBills");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoiceMatches_CompanyId_BillNumber",
                table: "PurchaseInvoiceMatches");

            migrationBuilder.DropIndex(
                name: "IX_LandedCostAllocations_CompanyId_BillNumber",
                table: "LandedCostAllocations");

            migrationBuilder.CreateIndex(
                name: "IX_VendorBills_CompanyId_VendorId_BillNumber",
                table: "VendorBills",
                columns: new[] { "CompanyId", "VendorId", "BillNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceMatches_CompanyId_VendorId_BillNumber",
                table: "PurchaseInvoiceMatches",
                columns: new[] { "CompanyId", "VendorId", "BillNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostAllocations_CompanyId_VendorId_BillNumber",
                table: "LandedCostAllocations",
                columns: new[] { "CompanyId", "VendorId", "BillNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException(
                "Downgrading vendor-scoped bill numbers is prohibited because forcing company-wide uniqueness could delete or misassociate bills from different vendors. Restore a backup taken before this migration instead.");
        }
    }
}
