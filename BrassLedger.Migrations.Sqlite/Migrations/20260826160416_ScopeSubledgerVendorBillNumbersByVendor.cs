using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class ScopeSubledgerVendorBillNumbersByVendor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubledgerDocumentWorkflows_CompanyId_DocumentType_DocumentNumber_IsRecurringTemplate",
                table: "SubledgerDocumentWorkflows");

            migrationBuilder.AddColumn<string>(
                name: "DocumentScope",
                table: "SubledgerDocumentWorkflows",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "company");

            migrationBuilder.Sql("""
                UPDATE "SubledgerDocumentWorkflows"
                SET "DocumentScope" = CASE
                    WHEN "DocumentType" = 'VendorBill' AND json_valid("PayloadJson")
                        THEN COALESCE(
                            lower(replace(json_extract("PayloadJson", '$.VendorId'), '-', '')),
                            lower(replace(json_extract("PayloadJson", '$.vendorId'), '-', '')),
                            'legacy')
                    ELSE 'company'
                END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SubledgerDocumentWorkflows_CompanyId_DocumentType_DocumentScope_DocumentNumber_IsRecurringTemplate",
                table: "SubledgerDocumentWorkflows",
                columns: new[] { "CompanyId", "DocumentType", "DocumentScope", "DocumentNumber", "IsRecurringTemplate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException(
                "Downgrading vendor-scoped subledger workflows is prohibited because restoring company-wide draft uniqueness could delete, discard, or misassociate legitimate bills from different vendors. Restore a backup taken before this migration instead.");
        }
    }
}
