using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InventoryReceiptLineId",
                table: "VendorBillLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CreditedQuantity",
                table: "PurchaseOrderLines",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ReturnedQuantity",
                table: "PurchaseOrderLines",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ReturnedQuantity",
                table: "InventoryReceiptLines",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "SupplierReturnAuthorizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnNumber = table.Column<string>(type: "text", nullable: false),
                    AuthorizedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    AuthorizedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorizedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CancelledByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierReturnAuthorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierReturnAuthorizations_InventoryReceipts_InventoryRec~",
                        column: x => x.InventoryReceiptId,
                        principalTable: "InventoryReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnAuthorizations_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnAuthorizations_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierReturnAuthorizationLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierReturnAuthorizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryReceiptLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    AuthorizedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ShippedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierReturnAuthorizationLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierReturnAuthorizationLines_InventoryItems_InventoryIt~",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnAuthorizationLines_InventoryReceiptLines_Inve~",
                        column: x => x.InventoryReceiptLineId,
                        principalTable: "InventoryReceiptLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnAuthorizationLines_PurchaseOrderLines_Purchas~",
                        column: x => x.PurchaseOrderLineId,
                        principalTable: "PurchaseOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnAuthorizationLines_SupplierReturnAuthorizatio~",
                        column: x => x.SupplierReturnAuthorizationId,
                        principalTable: "SupplierReturnAuthorizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupplierReturnShipments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierReturnAuthorizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceVendorBillId = table.Column<Guid>(type: "uuid", nullable: true),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    BinId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentNumber = table.Column<string>(type: "text", nullable: false),
                    ShippedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatesVendorCredit = table.Column<bool>(type: "boolean", nullable: false),
                    SourceAppliedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AppliedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RefundedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReversalJournalEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShippedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShippedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReversedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReversalDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ReversalReason = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierReturnShipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierReturnShipments_InventoryBins_BinId",
                        column: x => x.BinId,
                        principalTable: "InventoryBins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnShipments_InventoryWarehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "InventoryWarehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnShipments_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnShipments_JournalEntries_ReversalJournalEntry~",
                        column: x => x.ReversalJournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnShipments_SupplierReturnAuthorizations_Suppli~",
                        column: x => x.SupplierReturnAuthorizationId,
                        principalTable: "SupplierReturnAuthorizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnShipments_VendorBills_SourceVendorBillId",
                        column: x => x.SourceVendorBillId,
                        principalTable: "VendorBills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierReturnCreditApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierReturnShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorBillId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppliedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    AppliedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AppliedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReversedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReversalDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ReversalReason = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierReturnCreditApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierReturnCreditApplications_SupplierReturnShipments_Su~",
                        column: x => x.SupplierReturnShipmentId,
                        principalTable: "SupplierReturnShipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnCreditApplications_VendorBills_VendorBillId",
                        column: x => x.VendorBillId,
                        principalTable: "VendorBills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierReturnCreditRefunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierReturnShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "text", nullable: false),
                    RefundDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReversalJournalEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    RefundedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RefundedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReversedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReversalDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ReversalReason = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierReturnCreditRefunds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierReturnCreditRefunds_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnCreditRefunds_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnCreditRefunds_JournalEntries_ReversalJournalE~",
                        column: x => x.ReversalJournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnCreditRefunds_SupplierReturnShipments_Supplie~",
                        column: x => x.SupplierReturnShipmentId,
                        principalTable: "SupplierReturnShipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierReturnShipmentLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierReturnShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierReturnAuthorizationLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryReceiptLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PriorQuantityOnHand = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    PriorUnitCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ResultingUnitCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierReturnShipmentLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierReturnShipmentLines_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnShipmentLines_InventoryReceiptLines_Inventory~",
                        column: x => x.InventoryReceiptLineId,
                        principalTable: "InventoryReceiptLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnShipmentLines_PurchaseOrderLines_PurchaseOrde~",
                        column: x => x.PurchaseOrderLineId,
                        principalTable: "PurchaseOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnShipmentLines_SupplierReturnAuthorizationLine~",
                        column: x => x.SupplierReturnAuthorizationLineId,
                        principalTable: "SupplierReturnAuthorizationLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnShipmentLines_SupplierReturnShipments_Supplie~",
                        column: x => x.SupplierReturnShipmentId,
                        principalTable: "SupplierReturnShipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VendorBillLines_InventoryReceiptLineId",
                table: "VendorBillLines",
                column: "InventoryReceiptLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnAuthorizationLines_InventoryItemId",
                table: "SupplierReturnAuthorizationLines",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnAuthorizationLines_InventoryReceiptLineId",
                table: "SupplierReturnAuthorizationLines",
                column: "InventoryReceiptLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnAuthorizationLines_PurchaseOrderLineId",
                table: "SupplierReturnAuthorizationLines",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnAuthorizationLines_SupplierReturnAuthorizati~1",
                table: "SupplierReturnAuthorizationLines",
                columns: new[] { "SupplierReturnAuthorizationId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnAuthorizationLines_SupplierReturnAuthorizatio~",
                table: "SupplierReturnAuthorizationLines",
                columns: new[] { "SupplierReturnAuthorizationId", "InventoryReceiptLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnAuthorizations_CompanyId_ReturnNumber",
                table: "SupplierReturnAuthorizations",
                columns: new[] { "CompanyId", "ReturnNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnAuthorizations_InventoryReceiptId",
                table: "SupplierReturnAuthorizations",
                column: "InventoryReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnAuthorizations_PurchaseOrderId",
                table: "SupplierReturnAuthorizations",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnAuthorizations_VendorId",
                table: "SupplierReturnAuthorizations",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnCreditApplications_CompanyId_SupplierReturnSh~",
                table: "SupplierReturnCreditApplications",
                columns: new[] { "CompanyId", "SupplierReturnShipmentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnCreditApplications_SupplierReturnShipmentId",
                table: "SupplierReturnCreditApplications",
                column: "SupplierReturnShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnCreditApplications_VendorBillId",
                table: "SupplierReturnCreditApplications",
                column: "VendorBillId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnCreditRefunds_BankAccountId",
                table: "SupplierReturnCreditRefunds",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnCreditRefunds_CompanyId_Reference",
                table: "SupplierReturnCreditRefunds",
                columns: new[] { "CompanyId", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnCreditRefunds_JournalEntryId",
                table: "SupplierReturnCreditRefunds",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnCreditRefunds_ReversalJournalEntryId",
                table: "SupplierReturnCreditRefunds",
                column: "ReversalJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnCreditRefunds_SupplierReturnShipmentId",
                table: "SupplierReturnCreditRefunds",
                column: "SupplierReturnShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnShipmentLines_InventoryItemId",
                table: "SupplierReturnShipmentLines",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnShipmentLines_InventoryReceiptLineId",
                table: "SupplierReturnShipmentLines",
                column: "InventoryReceiptLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnShipmentLines_PurchaseOrderLineId",
                table: "SupplierReturnShipmentLines",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnShipmentLines_SupplierReturnAuthorizationLine~",
                table: "SupplierReturnShipmentLines",
                column: "SupplierReturnAuthorizationLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnShipmentLines_SupplierReturnShipmentId_Sequen~",
                table: "SupplierReturnShipmentLines",
                columns: new[] { "SupplierReturnShipmentId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnShipmentLines_SupplierReturnShipmentId_Suppli~",
                table: "SupplierReturnShipmentLines",
                columns: new[] { "SupplierReturnShipmentId", "SupplierReturnAuthorizationLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnShipments_BinId",
                table: "SupplierReturnShipments",
                column: "BinId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnShipments_CompanyId_ShipmentNumber",
                table: "SupplierReturnShipments",
                columns: new[] { "CompanyId", "ShipmentNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnShipments_JournalEntryId",
                table: "SupplierReturnShipments",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnShipments_ReversalJournalEntryId",
                table: "SupplierReturnShipments",
                column: "ReversalJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnShipments_SourceVendorBillId",
                table: "SupplierReturnShipments",
                column: "SourceVendorBillId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnShipments_SupplierReturnAuthorizationId",
                table: "SupplierReturnShipments",
                column: "SupplierReturnAuthorizationId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnShipments_WarehouseId",
                table: "SupplierReturnShipments",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_VendorBillLines_InventoryReceiptLines_InventoryReceiptLineId",
                table: "VendorBillLines",
                column: "InventoryReceiptLineId",
                principalTable: "InventoryReceiptLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VendorBillLines_InventoryReceiptLines_InventoryReceiptLineId",
                table: "VendorBillLines");

            migrationBuilder.DropTable(
                name: "SupplierReturnCreditApplications");

            migrationBuilder.DropTable(
                name: "SupplierReturnCreditRefunds");

            migrationBuilder.DropTable(
                name: "SupplierReturnShipmentLines");

            migrationBuilder.DropTable(
                name: "SupplierReturnAuthorizationLines");

            migrationBuilder.DropTable(
                name: "SupplierReturnShipments");

            migrationBuilder.DropTable(
                name: "SupplierReturnAuthorizations");

            migrationBuilder.DropIndex(
                name: "IX_VendorBillLines_InventoryReceiptLineId",
                table: "VendorBillLines");

            migrationBuilder.DropColumn(
                name: "InventoryReceiptLineId",
                table: "VendorBillLines");

            migrationBuilder.DropColumn(
                name: "CreditedQuantity",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "ReturnedQuantity",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "ReturnedQuantity",
                table: "InventoryReceiptLines");
        }
    }
}
