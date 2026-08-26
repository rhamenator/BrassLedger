using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseReceiving : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InventoryReceiptId",
                table: "VendorBills",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseOrderId",
                table: "VendorBills",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAtUtc",
                table: "PurchaseOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedByUserId",
                table: "PurchaseOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConcurrencyToken",
                table: "PurchaseOrders",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpectedOn",
                table: "PurchaseOrders",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "PurchaseOrders",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PreparedAtUtc",
                table: "PurchaseOrders",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "PreparedByUserId",
                table: "PurchaseOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCost",
                table: "InventoryItems",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // The legacy model used UnitPrice for both sales price and carrying cost.
            // Preserve that historical value as the opening average cost while separating the concepts.
            migrationBuilder.Sql("UPDATE \"InventoryItems\" SET \"UnitCost\" = \"UnitPrice\";");

            migrationBuilder.CreateTable(
                name: "InventoryReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptNumber = table.Column<string>(type: "text", nullable: false),
                    ReceivedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReversalJournalEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceivedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReversedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReversalDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ReversalReason = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryReceipts_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryReceipts_JournalEntries_ReversalJournalEntryId",
                        column: x => x.ReversalJournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryReceipts_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    OrderedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReceivedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    InvoicedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InventoryReceiptLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PriorQuantityOnHand = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    PriorUnitCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ResultingUnitCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryReceiptLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryReceiptLines_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryReceiptLines_InventoryReceipts_InventoryReceiptId",
                        column: x => x.InventoryReceiptId,
                        principalTable: "InventoryReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InventoryReceiptLines_PurchaseOrderLines_PurchaseOrderLineId",
                        column: x => x.PurchaseOrderLineId,
                        principalTable: "PurchaseOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VendorBills_InventoryReceiptId",
                table: "VendorBills",
                column: "InventoryReceiptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VendorBills_PurchaseOrderId",
                table: "VendorBills",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_VendorId",
                table: "PurchaseOrders",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReceiptLines_InventoryItemId",
                table: "InventoryReceiptLines",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReceiptLines_InventoryReceiptId_Sequence",
                table: "InventoryReceiptLines",
                columns: new[] { "InventoryReceiptId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReceiptLines_PurchaseOrderLineId",
                table: "InventoryReceiptLines",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReceipts_CompanyId_PurchaseOrderId_Status",
                table: "InventoryReceipts",
                columns: new[] { "CompanyId", "PurchaseOrderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReceipts_CompanyId_ReceiptNumber",
                table: "InventoryReceipts",
                columns: new[] { "CompanyId", "ReceiptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReceipts_JournalEntryId",
                table: "InventoryReceipts",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReceipts_PurchaseOrderId",
                table: "InventoryReceipts",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReceipts_ReversalJournalEntryId",
                table: "InventoryReceipts",
                column: "ReversalJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_InventoryItemId",
                table: "PurchaseOrderLines",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_PurchaseOrderId_Sequence",
                table: "PurchaseOrderLines",
                columns: new[] { "PurchaseOrderId", "Sequence" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_Vendors_VendorId",
                table: "PurchaseOrders",
                column: "VendorId",
                principalTable: "Vendors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VendorBills_InventoryReceipts_InventoryReceiptId",
                table: "VendorBills",
                column: "InventoryReceiptId",
                principalTable: "InventoryReceipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VendorBills_PurchaseOrders_PurchaseOrderId",
                table: "VendorBills",
                column: "PurchaseOrderId",
                principalTable: "PurchaseOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (!string.IsNullOrWhiteSpace(migrationBuilder.ActiveProvider))
                throw new NotSupportedException("Rolling back purchase receiving could delete receipt, match, and inventory valuation history and is prohibited. Restore a verified pre-upgrade backup instead.");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_Vendors_VendorId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_VendorBills_InventoryReceipts_InventoryReceiptId",
                table: "VendorBills");

            migrationBuilder.DropForeignKey(
                name: "FK_VendorBills_PurchaseOrders_PurchaseOrderId",
                table: "VendorBills");

            migrationBuilder.DropTable(
                name: "InventoryReceiptLines");

            migrationBuilder.DropTable(
                name: "InventoryReceipts");

            migrationBuilder.DropTable(
                name: "PurchaseOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_VendorBills_InventoryReceiptId",
                table: "VendorBills");

            migrationBuilder.DropIndex(
                name: "IX_VendorBills_PurchaseOrderId",
                table: "VendorBills");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_VendorId",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "InventoryReceiptId",
                table: "VendorBills");

            migrationBuilder.DropColumn(
                name: "PurchaseOrderId",
                table: "VendorBills");

            migrationBuilder.DropColumn(
                name: "ApprovedAtUtc",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "ExpectedOn",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "PreparedAtUtc",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "PreparedByUserId",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "UnitCost",
                table: "InventoryItems");
        }
    }
}
