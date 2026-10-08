using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesFulfillment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAtUtc",
                table: "SalesOrders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedByUserId",
                table: "SalesOrders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConcurrencyToken",
                table: "SalesOrders",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "SalesOrders",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PreparedAtUtc",
                table: "SalesOrders",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "PreparedByUserId",
                table: "SalesOrders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RequestedShipOn",
                table: "SalesOrders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InventoryShipmentId",
                table: "SalesInvoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesOrderId",
                table: "SalesInvoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InventoryItemId",
                table: "SalesInvoiceLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InventoryShipmentLineId",
                table: "SalesInvoiceLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesOrderLineId",
                table: "SalesInvoiceLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InventoryShipments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShipmentNumber = table.Column<string>(type: "TEXT", nullable: false),
                    ShippedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    TotalCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SalesInvoiceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReversalJournalEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ShippedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ShippedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReversedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReversedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReversalDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ReversalReason = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryShipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryShipments_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryShipments_JournalEntries_ReversalJournalEntryId",
                        column: x => x.ReversalJournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryShipments_SalesInvoices_SalesInvoiceId",
                        column: x => x.SalesInvoiceId,
                        principalTable: "SalesInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryShipments_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RevenueAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    OrderedQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    AllocatedQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ShippedQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ReturnedQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    InvoicedQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesOrderLines_Accounts_RevenueAccountId",
                        column: x => x.RevenueAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderLines_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderLines_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                UPDATE "SalesOrders"
                SET "Status" = 'LegacyReference',
                    "Notes" = 'Migrated header-only sales order retained for historical reference; create a line-based order before fulfillment.',
                    "ConcurrencyToken" = 'legacy-' || "Id";
                """);

            migrationBuilder.CreateTable(
                name: "InventoryShipmentLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InventoryShipmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SalesOrderLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TotalCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryShipmentLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryShipmentLines_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryShipmentLines_InventoryShipments_InventoryShipmentId",
                        column: x => x.InventoryShipmentId,
                        principalTable: "InventoryShipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InventoryShipmentLines_SalesOrderLines_SalesOrderLineId",
                        column: x => x.SalesOrderLineId,
                        principalTable: "SalesOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_CustomerId",
                table: "SalesOrders",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_InventoryShipmentId",
                table: "SalesInvoices",
                column: "InventoryShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_SalesOrderId",
                table: "SalesInvoices",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLines_InventoryItemId",
                table: "SalesInvoiceLines",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLines_InventoryShipmentLineId",
                table: "SalesInvoiceLines",
                column: "InventoryShipmentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLines_SalesOrderLineId",
                table: "SalesInvoiceLines",
                column: "SalesOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryShipmentLines_InventoryItemId",
                table: "InventoryShipmentLines",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryShipmentLines_InventoryShipmentId_Sequence",
                table: "InventoryShipmentLines",
                columns: new[] { "InventoryShipmentId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryShipmentLines_SalesOrderLineId",
                table: "InventoryShipmentLines",
                column: "SalesOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryShipments_CompanyId_SalesOrderId_Status",
                table: "InventoryShipments",
                columns: new[] { "CompanyId", "SalesOrderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryShipments_CompanyId_ShipmentNumber",
                table: "InventoryShipments",
                columns: new[] { "CompanyId", "ShipmentNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryShipments_JournalEntryId",
                table: "InventoryShipments",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryShipments_ReversalJournalEntryId",
                table: "InventoryShipments",
                column: "ReversalJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryShipments_SalesInvoiceId",
                table: "InventoryShipments",
                column: "SalesInvoiceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryShipments_SalesOrderId",
                table: "InventoryShipments",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_InventoryItemId",
                table: "SalesOrderLines",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_RevenueAccountId",
                table: "SalesOrderLines",
                column: "RevenueAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_SalesOrderId_Sequence",
                table: "SalesOrderLines",
                columns: new[] { "SalesOrderId", "Sequence" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceLines_InventoryItems_InventoryItemId",
                table: "SalesInvoiceLines",
                column: "InventoryItemId",
                principalTable: "InventoryItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceLines_InventoryShipmentLines_InventoryShipmentLineId",
                table: "SalesInvoiceLines",
                column: "InventoryShipmentLineId",
                principalTable: "InventoryShipmentLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceLines_SalesOrderLines_SalesOrderLineId",
                table: "SalesInvoiceLines",
                column: "SalesOrderLineId",
                principalTable: "SalesOrderLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoices_InventoryShipments_InventoryShipmentId",
                table: "SalesInvoices",
                column: "InventoryShipmentId",
                principalTable: "InventoryShipments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoices_SalesOrders_SalesOrderId",
                table: "SalesInvoices",
                column: "SalesOrderId",
                principalTable: "SalesOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrders_Customers_CustomerId",
                table: "SalesOrders",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (!string.IsNullOrWhiteSpace(migrationBuilder.ActiveProvider))
                throw new NotSupportedException("Rolling back sales fulfillment could delete allocation, shipment, invoice-provenance, and inventory valuation history and is prohibited. Restore a verified pre-upgrade backup instead.");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceLines_InventoryItems_InventoryItemId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceLines_InventoryShipmentLines_InventoryShipmentLineId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceLines_SalesOrderLines_SalesOrderLineId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoices_InventoryShipments_InventoryShipmentId",
                table: "SalesInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoices_SalesOrders_SalesOrderId",
                table: "SalesInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrders_Customers_CustomerId",
                table: "SalesOrders");

            migrationBuilder.DropTable(
                name: "InventoryShipmentLines");

            migrationBuilder.DropTable(
                name: "InventoryShipments");

            migrationBuilder.DropTable(
                name: "SalesOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrders_CustomerId",
                table: "SalesOrders");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoices_InventoryShipmentId",
                table: "SalesInvoices");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoices_SalesOrderId",
                table: "SalesInvoices");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceLines_InventoryItemId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceLines_InventoryShipmentLineId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceLines_SalesOrderLineId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "ApprovedAtUtc",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "PreparedAtUtc",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "PreparedByUserId",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "RequestedShipOn",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "InventoryShipmentId",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "SalesOrderId",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "InventoryItemId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "InventoryShipmentLineId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "SalesOrderLineId",
                table: "SalesInvoiceLines");
        }
    }
}
