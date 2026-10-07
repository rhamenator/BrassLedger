using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddControlledPurchaseInvoiceMatching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VendorBills_InventoryReceiptId",
                table: "VendorBills");

            migrationBuilder.AddColumn<decimal>(
                name: "AccrualAmount",
                table: "VendorBillLines",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MatchedQuantity",
                table: "VendorBillLines",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PriceVarianceAmount",
                table: "VendorBillLines",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "QuantityVarianceAmount",
                table: "VendorBillLines",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "QuantityVarianceQuantity",
                table: "VendorBillLines",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ReceiptUnitCost",
                table: "VendorBillLines",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql(
                """
                UPDATE "VendorBillLines"
                SET "MatchedQuantity" = "Quantity",
                    "ReceiptUnitCost" = "UnitCost",
                    "AccrualAmount" = "LineTotal"
                WHERE "InventoryReceiptLineId" IS NOT NULL
                  AND EXISTS (
                      SELECT 1 FROM "VendorBills"
                      WHERE "VendorBills"."Id" = "VendorBillLines"."VendorBillId"
                        AND "VendorBills"."InventoryReceiptId" IS NOT NULL
                  );
                """);

            migrationBuilder.AddColumn<decimal>(
                name: "GrniReductionAmount",
                table: "SupplierReturnShipmentLines",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "InvoicedQuantity",
                table: "SupplierReturnShipmentLines",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql(
                """
                UPDATE "SupplierReturnShipmentLines"
                SET "InvoicedQuantity" = CASE
                        WHEN EXISTS (SELECT 1 FROM "SupplierReturnShipments" WHERE "SupplierReturnShipments"."Id" = "SupplierReturnShipmentLines"."SupplierReturnShipmentId" AND "SupplierReturnShipments"."CreatesVendorCredit" = 1)
                        THEN "Quantity" ELSE 0 END,
                    "GrniReductionAmount" = CASE
                        WHEN EXISTS (SELECT 1 FROM "SupplierReturnShipments" WHERE "SupplierReturnShipments"."Id" = "SupplierReturnShipmentLines"."SupplierReturnShipmentId" AND "SupplierReturnShipments"."CreatesVendorCredit" = 0)
                        THEN "VendorCreditAmount" ELSE 0 END;
                """);

            migrationBuilder.CreateTable(
                name: "PurchaseInvoiceMatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InventoryReceiptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VendorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VendorBillId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BillNumber = table.Column<string>(type: "TEXT", nullable: false),
                    BillDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    InvoiceAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    AccrualAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    PriceVarianceAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    QuantityVarianceQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    QuantityVarianceAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    SourceReceiptConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false),
                    PreparedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DecisionReason = table.Column<string>(type: "TEXT", nullable: false),
                    PostedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PostedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    JournalEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CancellationReason = table.Column<string>(type: "TEXT", nullable: false),
                    ReversalJournalEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReversedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReversedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReversalDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ReversalReason = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoiceMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceMatches_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceMatches_InventoryReceipts_InventoryReceiptId",
                        column: x => x.InventoryReceiptId,
                        principalTable: "InventoryReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceMatches_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceMatches_JournalEntries_ReversalJournalEntryId",
                        column: x => x.ReversalJournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceMatches_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceMatches_VendorBills_VendorBillId",
                        column: x => x.VendorBillId,
                        principalTable: "VendorBills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceMatches_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseInvoiceMatchLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PurchaseInvoiceMatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InventoryReceiptLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PurchaseOrderLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    AvailableQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    InvoiceQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    MatchedQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    QuantityVarianceQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ReceiptUnitCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    InvoiceUnitCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    AccrualAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    InvoiceAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    PriceVarianceAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    QuantityVarianceAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoiceMatchLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceMatchLines_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceMatchLines_InventoryReceiptLines_InventoryReceiptLineId",
                        column: x => x.InventoryReceiptLineId,
                        principalTable: "InventoryReceiptLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceMatchLines_PurchaseInvoiceMatches_PurchaseInvoiceMatchId",
                        column: x => x.PurchaseInvoiceMatchId,
                        principalTable: "PurchaseInvoiceMatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceMatchLines_PurchaseOrderLines_PurchaseOrderLineId",
                        column: x => x.PurchaseOrderLineId,
                        principalTable: "PurchaseOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VendorBills_InventoryReceiptId",
                table: "VendorBills",
                column: "InventoryReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceMatches_CompanyId_BillNumber",
                table: "PurchaseInvoiceMatches",
                columns: new[] { "CompanyId", "BillNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceMatches_CompanyId_InventoryReceiptId_Status",
                table: "PurchaseInvoiceMatches",
                columns: new[] { "CompanyId", "InventoryReceiptId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceMatches_InventoryReceiptId",
                table: "PurchaseInvoiceMatches",
                column: "InventoryReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceMatches_JournalEntryId",
                table: "PurchaseInvoiceMatches",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceMatches_PurchaseOrderId",
                table: "PurchaseInvoiceMatches",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceMatches_ReversalJournalEntryId",
                table: "PurchaseInvoiceMatches",
                column: "ReversalJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceMatches_VendorBillId",
                table: "PurchaseInvoiceMatches",
                column: "VendorBillId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceMatches_VendorId",
                table: "PurchaseInvoiceMatches",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceMatchLines_InventoryItemId",
                table: "PurchaseInvoiceMatchLines",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceMatchLines_InventoryReceiptLineId",
                table: "PurchaseInvoiceMatchLines",
                column: "InventoryReceiptLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceMatchLines_PurchaseInvoiceMatchId_InventoryReceiptLineId",
                table: "PurchaseInvoiceMatchLines",
                columns: new[] { "PurchaseInvoiceMatchId", "InventoryReceiptLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceMatchLines_PurchaseInvoiceMatchId_Sequence",
                table: "PurchaseInvoiceMatchLines",
                columns: new[] { "PurchaseInvoiceMatchId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceMatchLines_PurchaseOrderLineId",
                table: "PurchaseInvoiceMatchLines",
                column: "PurchaseOrderLineId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Downgrading controlled purchase-invoice matching is prohibited because it could delete match, variance, and supplier-return provenance. Restore a backup taken before this migration instead.");
        }
    }
}
