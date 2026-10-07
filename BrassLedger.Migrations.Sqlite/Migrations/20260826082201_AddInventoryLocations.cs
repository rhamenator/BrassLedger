using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AllocationBinId",
                table: "SalesOrderLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AllocationWarehouseId",
                table: "SalesOrderLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "JournalEntryId",
                table: "InventoryTransactions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<Guid>(
                name: "BinId",
                table: "InventoryTransactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InventoryTransferId",
                table: "InventoryTransactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "InventoryTransactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BinId",
                table: "InventoryShipments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "InventoryShipments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BinId",
                table: "InventoryReceipts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "InventoryReceipts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConcurrencyToken",
                table: "InventoryItems",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "InventoryWarehouses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    AddressLine1 = table.Column<string>(type: "TEXT", nullable: false),
                    AddressLine2 = table.Column<string>(type: "TEXT", nullable: false),
                    City = table.Column<string>(type: "TEXT", nullable: false),
                    StateOrProvince = table.Column<string>(type: "TEXT", nullable: false),
                    PostalCode = table.Column<string>(type: "TEXT", nullable: false),
                    CountryCode = table.Column<string>(type: "TEXT", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    DefaultMarker = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryWarehouses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InventoryBins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    DefaultMarker = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryBins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryBins_InventoryWarehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "InventoryWarehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryLocationBalances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BinId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuantityOnHand = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryLocationBalances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryLocationBalances_InventoryBins_BinId",
                        column: x => x.BinId,
                        principalTable: "InventoryBins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryLocationBalances_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryLocationBalances_InventoryWarehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "InventoryWarehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceWarehouseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceBinId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DestinationWarehouseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DestinationBinId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TransferDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    TransferredByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TransferredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReversedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReversedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReversalDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ReversalReason = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryTransfers_InventoryBins_DestinationBinId",
                        column: x => x.DestinationBinId,
                        principalTable: "InventoryBins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransfers_InventoryBins_SourceBinId",
                        column: x => x.SourceBinId,
                        principalTable: "InventoryBins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransfers_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransfers_InventoryWarehouses_DestinationWarehouseId",
                        column: x => x.DestinationWarehouseId,
                        principalTable: "InventoryWarehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransfers_InventoryWarehouses_SourceWarehouseId",
                        column: x => x.SourceWarehouseId,
                        principalTable: "InventoryWarehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_AllocationBinId",
                table: "SalesOrderLines",
                column: "AllocationBinId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_AllocationWarehouseId",
                table: "SalesOrderLines",
                column: "AllocationWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_BinId",
                table: "InventoryTransactions",
                column: "BinId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_InventoryTransferId",
                table: "InventoryTransactions",
                column: "InventoryTransferId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_WarehouseId",
                table: "InventoryTransactions",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryShipments_BinId",
                table: "InventoryShipments",
                column: "BinId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryShipments_WarehouseId",
                table: "InventoryShipments",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReceipts_BinId",
                table: "InventoryReceipts",
                column: "BinId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReceipts_WarehouseId",
                table: "InventoryReceipts",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBins_WarehouseId_Code",
                table: "InventoryBins",
                columns: new[] { "WarehouseId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBins_WarehouseId_DefaultMarker",
                table: "InventoryBins",
                columns: new[] { "WarehouseId", "DefaultMarker" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLocationBalances_BinId",
                table: "InventoryLocationBalances",
                column: "BinId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLocationBalances_CompanyId_WarehouseId_InventoryItemId",
                table: "InventoryLocationBalances",
                columns: new[] { "CompanyId", "WarehouseId", "InventoryItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLocationBalances_InventoryItemId_BinId",
                table: "InventoryLocationBalances",
                columns: new[] { "InventoryItemId", "BinId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLocationBalances_WarehouseId",
                table: "InventoryLocationBalances",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransfers_CompanyId_InventoryItemId_TransferDate",
                table: "InventoryTransfers",
                columns: new[] { "CompanyId", "InventoryItemId", "TransferDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransfers_CompanyId_Reference",
                table: "InventoryTransfers",
                columns: new[] { "CompanyId", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransfers_DestinationBinId",
                table: "InventoryTransfers",
                column: "DestinationBinId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransfers_DestinationWarehouseId",
                table: "InventoryTransfers",
                column: "DestinationWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransfers_InventoryItemId",
                table: "InventoryTransfers",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransfers_SourceBinId",
                table: "InventoryTransfers",
                column: "SourceBinId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransfers_SourceWarehouseId",
                table: "InventoryTransfers",
                column: "SourceWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryWarehouses_CompanyId_Code",
                table: "InventoryWarehouses",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryWarehouses_CompanyId_DefaultMarker",
                table: "InventoryWarehouses",
                columns: new[] { "CompanyId", "DefaultMarker" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryReceipts_InventoryBins_BinId",
                table: "InventoryReceipts",
                column: "BinId",
                principalTable: "InventoryBins",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryReceipts_InventoryWarehouses_WarehouseId",
                table: "InventoryReceipts",
                column: "WarehouseId",
                principalTable: "InventoryWarehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryShipments_InventoryBins_BinId",
                table: "InventoryShipments",
                column: "BinId",
                principalTable: "InventoryBins",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryShipments_InventoryWarehouses_WarehouseId",
                table: "InventoryShipments",
                column: "WarehouseId",
                principalTable: "InventoryWarehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryTransactions_InventoryBins_BinId",
                table: "InventoryTransactions",
                column: "BinId",
                principalTable: "InventoryBins",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryTransactions_InventoryTransfers_InventoryTransferId",
                table: "InventoryTransactions",
                column: "InventoryTransferId",
                principalTable: "InventoryTransfers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryTransactions_InventoryWarehouses_WarehouseId",
                table: "InventoryTransactions",
                column: "WarehouseId",
                principalTable: "InventoryWarehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrderLines_InventoryBins_AllocationBinId",
                table: "SalesOrderLines",
                column: "AllocationBinId",
                principalTable: "InventoryBins",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrderLines_InventoryWarehouses_AllocationWarehouseId",
                table: "SalesOrderLines",
                column: "AllocationWarehouseId",
                principalTable: "InventoryWarehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider is not null)
                throw new NotSupportedException("Rolling back inventory locations could delete warehouse balances and transfer history and is prohibited. Restore a verified pre-upgrade backup instead.");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryReceipts_InventoryBins_BinId",
                table: "InventoryReceipts");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryReceipts_InventoryWarehouses_WarehouseId",
                table: "InventoryReceipts");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryShipments_InventoryBins_BinId",
                table: "InventoryShipments");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryShipments_InventoryWarehouses_WarehouseId",
                table: "InventoryShipments");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryTransactions_InventoryBins_BinId",
                table: "InventoryTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryTransactions_InventoryTransfers_InventoryTransferId",
                table: "InventoryTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryTransactions_InventoryWarehouses_WarehouseId",
                table: "InventoryTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrderLines_InventoryBins_AllocationBinId",
                table: "SalesOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrderLines_InventoryWarehouses_AllocationWarehouseId",
                table: "SalesOrderLines");

            migrationBuilder.DropTable(
                name: "InventoryLocationBalances");

            migrationBuilder.DropTable(
                name: "InventoryTransfers");

            migrationBuilder.DropTable(
                name: "InventoryBins");

            migrationBuilder.DropTable(
                name: "InventoryWarehouses");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrderLines_AllocationBinId",
                table: "SalesOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrderLines_AllocationWarehouseId",
                table: "SalesOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_BinId",
                table: "InventoryTransactions");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_InventoryTransferId",
                table: "InventoryTransactions");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_WarehouseId",
                table: "InventoryTransactions");

            migrationBuilder.DropIndex(
                name: "IX_InventoryShipments_BinId",
                table: "InventoryShipments");

            migrationBuilder.DropIndex(
                name: "IX_InventoryShipments_WarehouseId",
                table: "InventoryShipments");

            migrationBuilder.DropIndex(
                name: "IX_InventoryReceipts_BinId",
                table: "InventoryReceipts");

            migrationBuilder.DropIndex(
                name: "IX_InventoryReceipts_WarehouseId",
                table: "InventoryReceipts");

            migrationBuilder.DropColumn(
                name: "AllocationBinId",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "AllocationWarehouseId",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "BinId",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "InventoryTransferId",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "BinId",
                table: "InventoryShipments");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "InventoryShipments");

            migrationBuilder.DropColumn(
                name: "BinId",
                table: "InventoryReceipts");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "InventoryReceipts");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "InventoryItems");

            migrationBuilder.AlterColumn<Guid>(
                name: "JournalEntryId",
                table: "InventoryTransactions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
