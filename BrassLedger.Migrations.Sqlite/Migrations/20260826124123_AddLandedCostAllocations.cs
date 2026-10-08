using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddLandedCostAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LandedCostAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InventoryReceiptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VendorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VendorBillId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AllocationNumber = table.Column<string>(type: "TEXT", nullable: false),
                    BillNumber = table.Column<string>(type: "TEXT", nullable: false),
                    BillDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    AllocationMethod = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
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
                    ReversalJournalEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReversedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReversedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReversalDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ReversalReason = table.Column<string>(type: "TEXT", nullable: false),
                    CancelledByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CancellationReason = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LandedCostAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LandedCostAllocations_InventoryReceipts_InventoryReceiptId",
                        column: x => x.InventoryReceiptId,
                        principalTable: "InventoryReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LandedCostAllocations_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LandedCostAllocations_JournalEntries_ReversalJournalEntryId",
                        column: x => x.ReversalJournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LandedCostAllocations_VendorBills_VendorBillId",
                        column: x => x.VendorBillId,
                        principalTable: "VendorBills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LandedCostAllocations_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LandedCostAllocationLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LandedCostAllocationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InventoryReceiptLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PurchaseOrderLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    BasisQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    BasisAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    AllocatedAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    PreparedItemConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false),
                    PriorQuantityOnHand = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    PriorUnitCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ResultingUnitCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LandedCostAllocationLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LandedCostAllocationLines_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LandedCostAllocationLines_InventoryReceiptLines_InventoryReceiptLineId",
                        column: x => x.InventoryReceiptLineId,
                        principalTable: "InventoryReceiptLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LandedCostAllocationLines_LandedCostAllocations_LandedCostAllocationId",
                        column: x => x.LandedCostAllocationId,
                        principalTable: "LandedCostAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LandedCostAllocationLines_PurchaseOrderLines_PurchaseOrderLineId",
                        column: x => x.PurchaseOrderLineId,
                        principalTable: "PurchaseOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LandedCostCharges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LandedCostAllocationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    ChargeType = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LandedCostCharges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LandedCostCharges_LandedCostAllocations_LandedCostAllocationId",
                        column: x => x.LandedCostAllocationId,
                        principalTable: "LandedCostAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostAllocationLines_InventoryItemId",
                table: "LandedCostAllocationLines",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostAllocationLines_InventoryReceiptLineId",
                table: "LandedCostAllocationLines",
                column: "InventoryReceiptLineId");

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostAllocationLines_LandedCostAllocationId_InventoryReceiptLineId",
                table: "LandedCostAllocationLines",
                columns: new[] { "LandedCostAllocationId", "InventoryReceiptLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostAllocationLines_LandedCostAllocationId_Sequence",
                table: "LandedCostAllocationLines",
                columns: new[] { "LandedCostAllocationId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostAllocationLines_PurchaseOrderLineId",
                table: "LandedCostAllocationLines",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostAllocations_CompanyId_AllocationNumber",
                table: "LandedCostAllocations",
                columns: new[] { "CompanyId", "AllocationNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostAllocations_CompanyId_BillNumber",
                table: "LandedCostAllocations",
                columns: new[] { "CompanyId", "BillNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostAllocations_CompanyId_InventoryReceiptId_Status",
                table: "LandedCostAllocations",
                columns: new[] { "CompanyId", "InventoryReceiptId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostAllocations_InventoryReceiptId",
                table: "LandedCostAllocations",
                column: "InventoryReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostAllocations_JournalEntryId",
                table: "LandedCostAllocations",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostAllocations_ReversalJournalEntryId",
                table: "LandedCostAllocations",
                column: "ReversalJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostAllocations_VendorBillId",
                table: "LandedCostAllocations",
                column: "VendorBillId");

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostAllocations_VendorId",
                table: "LandedCostAllocations",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostCharges_LandedCostAllocationId_Sequence",
                table: "LandedCostCharges",
                columns: new[] { "LandedCostAllocationId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LandedCostAllocationLines");

            migrationBuilder.DropTable(
                name: "LandedCostCharges");

            migrationBuilder.DropTable(
                name: "LandedCostAllocations");
        }
    }
}
