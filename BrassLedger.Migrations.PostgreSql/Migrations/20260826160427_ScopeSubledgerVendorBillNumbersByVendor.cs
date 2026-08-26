using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class ScopeSubledgerVendorBillNumbersByVendor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubledgerDocumentWorkflows_CompanyId_DocumentType_DocumentN~",
                table: "SubledgerDocumentWorkflows");

            migrationBuilder.AddColumn<string>(
                name: "DocumentScope",
                table: "SubledgerDocumentWorkflows",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "company");

            migrationBuilder.Sql("""
                UPDATE "SubledgerDocumentWorkflows"
                SET "DocumentScope" = CASE
                    WHEN "DocumentType" = 'VendorBill'
                        THEN COALESCE(
                            lower(replace(substring("PayloadJson" from '(?i)"vendorid"\s*:\s*"([0-9a-f-]+)"'), '-', '')),
                            'legacy')
                    ELSE 'company'
                END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SubledgerDocumentWorkflows_CompanyId_DocumentType_DocumentS~",
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
