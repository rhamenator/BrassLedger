using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ReversalDate",
                table: "SubledgerPayments",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomerReturnAuthorizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_CustomerReturnAuthorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerReturnAuthorizations_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnAuthorizations_InventoryShipments_InventorySh~",
                        column: x => x.InventoryShipmentId,
                        principalTable: "InventoryShipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnAuthorizations_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerReturnAuthorizationLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerReturnAuthorizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryShipmentLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    AuthorizedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ReceivedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerReturnAuthorizationLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerReturnAuthorizationLines_CustomerReturnAuthorizatio~",
                        column: x => x.CustomerReturnAuthorizationId,
                        principalTable: "CustomerReturnAuthorizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomerReturnAuthorizationLines_InventoryItems_InventoryIt~",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnAuthorizationLines_InventoryShipmentLines_Inv~",
                        column: x => x.InventoryShipmentLineId,
                        principalTable: "InventoryShipmentLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnAuthorizationLines_SalesOrderLines_SalesOrder~",
                        column: x => x.SalesOrderLineId,
                        principalTable: "SalesOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerReturnReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerReturnAuthorizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    BinId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptNumber = table.Column<string>(type: "text", nullable: false),
                    ReceivedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    TotalCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
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
                    table.PrimaryKey("PK_CustomerReturnReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerReturnReceipts_CustomerReturnAuthorizations_Custome~",
                        column: x => x.CustomerReturnAuthorizationId,
                        principalTable: "CustomerReturnAuthorizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnReceipts_InventoryBins_BinId",
                        column: x => x.BinId,
                        principalTable: "InventoryBins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnReceipts_InventoryWarehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "InventoryWarehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnReceipts_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnReceipts_JournalEntries_ReversalJournalEntryId",
                        column: x => x.ReversalJournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerReturnCredits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerReturnReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesInvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditNumber = table.Column<string>(type: "text", nullable: false),
                    CreditDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SourceAppliedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AppliedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RefundedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReversalJournalEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReversedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReversalDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ReversalReason = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerReturnCredits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerReturnCredits_CustomerReturnReceipts_CustomerReturn~",
                        column: x => x.CustomerReturnReceiptId,
                        principalTable: "CustomerReturnReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnCredits_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnCredits_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnCredits_JournalEntries_ReversalJournalEntryId",
                        column: x => x.ReversalJournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnCredits_SalesInvoices_SalesInvoiceId",
                        column: x => x.SalesInvoiceId,
                        principalTable: "SalesInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerReturnReceiptLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerReturnReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerReturnAuthorizationLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryShipmentLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerReturnReceiptLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerReturnReceiptLines_CustomerReturnAuthorizationLines~",
                        column: x => x.CustomerReturnAuthorizationLineId,
                        principalTable: "CustomerReturnAuthorizationLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnReceiptLines_CustomerReturnReceipts_CustomerR~",
                        column: x => x.CustomerReturnReceiptId,
                        principalTable: "CustomerReturnReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomerReturnReceiptLines_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnReceiptLines_InventoryShipmentLines_Inventory~",
                        column: x => x.InventoryShipmentLineId,
                        principalTable: "InventoryShipmentLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnReceiptLines_SalesOrderLines_SalesOrderLineId",
                        column: x => x.SalesOrderLineId,
                        principalTable: "SalesOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerReturnCreditApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerReturnCreditId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesInvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_CustomerReturnCreditApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerReturnCreditApplications_CustomerReturnCredits_Cust~",
                        column: x => x.CustomerReturnCreditId,
                        principalTable: "CustomerReturnCredits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnCreditApplications_SalesInvoices_SalesInvoice~",
                        column: x => x.SalesInvoiceId,
                        principalTable: "SalesInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerReturnCreditRefunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerReturnCreditId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    ReversalReason = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerReturnCreditRefunds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerReturnCreditRefunds_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnCreditRefunds_CustomerReturnCredits_CustomerR~",
                        column: x => x.CustomerReturnCreditId,
                        principalTable: "CustomerReturnCredits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnCreditRefunds_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnCreditRefunds_JournalEntries_ReversalJournalE~",
                        column: x => x.ReversalJournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerReturnCreditLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerReturnCreditId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerReturnReceiptLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesInvoiceLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevenueAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    NetAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerReturnCreditLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerReturnCreditLines_Accounts_RevenueAccountId",
                        column: x => x.RevenueAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnCreditLines_CustomerReturnCredits_CustomerRet~",
                        column: x => x.CustomerReturnCreditId,
                        principalTable: "CustomerReturnCredits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomerReturnCreditLines_CustomerReturnReceiptLines_Custom~",
                        column: x => x.CustomerReturnReceiptLineId,
                        principalTable: "CustomerReturnReceiptLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnCreditLines_SalesInvoiceLines_SalesInvoiceLin~",
                        column: x => x.SalesInvoiceLineId,
                        principalTable: "SalesInvoiceLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnAuthorizationLines_CustomerReturnAuthorizatio~",
                table: "CustomerReturnAuthorizationLines",
                columns: new[] { "CustomerReturnAuthorizationId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnAuthorizationLines_InventoryItemId",
                table: "CustomerReturnAuthorizationLines",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnAuthorizationLines_InventoryShipmentLineId",
                table: "CustomerReturnAuthorizationLines",
                column: "InventoryShipmentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnAuthorizationLines_SalesOrderLineId",
                table: "CustomerReturnAuthorizationLines",
                column: "SalesOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnAuthorizations_CompanyId_InventoryShipmentId_~",
                table: "CustomerReturnAuthorizations",
                columns: new[] { "CompanyId", "InventoryShipmentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnAuthorizations_CompanyId_ReturnNumber",
                table: "CustomerReturnAuthorizations",
                columns: new[] { "CompanyId", "ReturnNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnAuthorizations_CustomerId",
                table: "CustomerReturnAuthorizations",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnAuthorizations_InventoryShipmentId",
                table: "CustomerReturnAuthorizations",
                column: "InventoryShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnAuthorizations_SalesOrderId",
                table: "CustomerReturnAuthorizations",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnCreditApplications_CompanyId_CustomerReturnCr~",
                table: "CustomerReturnCreditApplications",
                columns: new[] { "CompanyId", "CustomerReturnCreditId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnCreditApplications_CustomerReturnCreditId",
                table: "CustomerReturnCreditApplications",
                column: "CustomerReturnCreditId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnCreditApplications_SalesInvoiceId",
                table: "CustomerReturnCreditApplications",
                column: "SalesInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnCreditLines_CustomerReturnCreditId_Sequence",
                table: "CustomerReturnCreditLines",
                columns: new[] { "CustomerReturnCreditId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnCreditLines_CustomerReturnReceiptLineId",
                table: "CustomerReturnCreditLines",
                column: "CustomerReturnReceiptLineId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnCreditLines_RevenueAccountId",
                table: "CustomerReturnCreditLines",
                column: "RevenueAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnCreditLines_SalesInvoiceLineId",
                table: "CustomerReturnCreditLines",
                column: "SalesInvoiceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnCreditRefunds_BankAccountId",
                table: "CustomerReturnCreditRefunds",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnCreditRefunds_CompanyId_Reference",
                table: "CustomerReturnCreditRefunds",
                columns: new[] { "CompanyId", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnCreditRefunds_CustomerReturnCreditId",
                table: "CustomerReturnCreditRefunds",
                column: "CustomerReturnCreditId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnCreditRefunds_JournalEntryId",
                table: "CustomerReturnCreditRefunds",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnCreditRefunds_ReversalJournalEntryId",
                table: "CustomerReturnCreditRefunds",
                column: "ReversalJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnCredits_CompanyId_CreditNumber",
                table: "CustomerReturnCredits",
                columns: new[] { "CompanyId", "CreditNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnCredits_CompanyId_CustomerReturnReceiptId_Sta~",
                table: "CustomerReturnCredits",
                columns: new[] { "CompanyId", "CustomerReturnReceiptId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnCredits_CustomerId",
                table: "CustomerReturnCredits",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnCredits_CustomerReturnReceiptId",
                table: "CustomerReturnCredits",
                column: "CustomerReturnReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnCredits_JournalEntryId",
                table: "CustomerReturnCredits",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnCredits_ReversalJournalEntryId",
                table: "CustomerReturnCredits",
                column: "ReversalJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnCredits_SalesInvoiceId",
                table: "CustomerReturnCredits",
                column: "SalesInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnReceiptLines_CustomerReturnAuthorizationLineId",
                table: "CustomerReturnReceiptLines",
                column: "CustomerReturnAuthorizationLineId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnReceiptLines_CustomerReturnReceiptId_Sequence",
                table: "CustomerReturnReceiptLines",
                columns: new[] { "CustomerReturnReceiptId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnReceiptLines_InventoryItemId",
                table: "CustomerReturnReceiptLines",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnReceiptLines_InventoryShipmentLineId",
                table: "CustomerReturnReceiptLines",
                column: "InventoryShipmentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnReceiptLines_SalesOrderLineId",
                table: "CustomerReturnReceiptLines",
                column: "SalesOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnReceipts_BinId",
                table: "CustomerReturnReceipts",
                column: "BinId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnReceipts_CompanyId_CustomerReturnAuthorizatio~",
                table: "CustomerReturnReceipts",
                columns: new[] { "CompanyId", "CustomerReturnAuthorizationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnReceipts_CompanyId_ReceiptNumber",
                table: "CustomerReturnReceipts",
                columns: new[] { "CompanyId", "ReceiptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnReceipts_CustomerReturnAuthorizationId",
                table: "CustomerReturnReceipts",
                column: "CustomerReturnAuthorizationId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnReceipts_JournalEntryId",
                table: "CustomerReturnReceipts",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnReceipts_ReversalJournalEntryId",
                table: "CustomerReturnReceipts",
                column: "ReversalJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnReceipts_WarehouseId",
                table: "CustomerReturnReceipts",
                column: "WarehouseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerReturnCreditApplications");

            migrationBuilder.DropTable(
                name: "CustomerReturnCreditLines");

            migrationBuilder.DropTable(
                name: "CustomerReturnCreditRefunds");

            migrationBuilder.DropTable(
                name: "CustomerReturnReceiptLines");

            migrationBuilder.DropTable(
                name: "CustomerReturnCredits");

            migrationBuilder.DropTable(
                name: "CustomerReturnAuthorizationLines");

            migrationBuilder.DropTable(
                name: "CustomerReturnReceipts");

            migrationBuilder.DropTable(
                name: "CustomerReturnAuthorizations");

            migrationBuilder.DropColumn(
                name: "ReversalDate",
                table: "SubledgerPayments");
        }
    }
}
