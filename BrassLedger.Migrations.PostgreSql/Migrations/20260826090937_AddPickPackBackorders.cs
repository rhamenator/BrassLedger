using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddPickPackBackorders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InventoryPackingSlipId",
                table: "InventoryShipments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InventoryPicks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    BinId = table.Column<Guid>(type: "uuid", nullable: false),
                    PickNumber = table.Column<string>(type: "text", nullable: false),
                    PickDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryPicks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryPicks_InventoryBins_BinId",
                        column: x => x.BinId,
                        principalTable: "InventoryBins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryPicks_InventoryWarehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "InventoryWarehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryPicks_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderBackorderPromises",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    PromisedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    FulfilledQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    PromisedShipOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CancelledByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderBackorderPromises", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesOrderBackorderPromises_SalesOrderLines_SalesOrderLineId",
                        column: x => x.SalesOrderLineId,
                        principalTable: "SalesOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderBackorderPromises_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryPackingSlips",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryPickId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    BinId = table.Column<Guid>(type: "uuid", nullable: false),
                    PackingSlipNumber = table.Column<string>(type: "text", nullable: false),
                    PackedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    PackedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    PackedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CancelledByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryPackingSlips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryPackingSlips_InventoryBins_BinId",
                        column: x => x.BinId,
                        principalTable: "InventoryBins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryPackingSlips_InventoryPicks_InventoryPickId",
                        column: x => x.InventoryPickId,
                        principalTable: "InventoryPicks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryPackingSlips_InventoryWarehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "InventoryWarehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryPackingSlips_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryPickLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryPickId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    RequestedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    PickedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryPickLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryPickLines_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryPickLines_InventoryPicks_InventoryPickId",
                        column: x => x.InventoryPickId,
                        principalTable: "InventoryPicks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InventoryPickLines_SalesOrderLines_SalesOrderLineId",
                        column: x => x.SalesOrderLineId,
                        principalTable: "SalesOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryPackingSlipLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryPackingSlipId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryPickLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryPackingSlipLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryPackingSlipLines_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryPackingSlipLines_InventoryPackingSlips_InventoryPa~",
                        column: x => x.InventoryPackingSlipId,
                        principalTable: "InventoryPackingSlips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InventoryPackingSlipLines_InventoryPickLines_InventoryPickL~",
                        column: x => x.InventoryPickLineId,
                        principalTable: "InventoryPickLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryPackingSlipLines_SalesOrderLines_SalesOrderLineId",
                        column: x => x.SalesOrderLineId,
                        principalTable: "SalesOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryShipments_InventoryPackingSlipId",
                table: "InventoryShipments",
                column: "InventoryPackingSlipId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryPackingSlipLines_InventoryItemId",
                table: "InventoryPackingSlipLines",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryPackingSlipLines_InventoryPackingSlipId",
                table: "InventoryPackingSlipLines",
                column: "InventoryPackingSlipId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryPackingSlipLines_InventoryPickLineId",
                table: "InventoryPackingSlipLines",
                column: "InventoryPickLineId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryPackingSlipLines_SalesOrderLineId",
                table: "InventoryPackingSlipLines",
                column: "SalesOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryPackingSlips_BinId",
                table: "InventoryPackingSlips",
                column: "BinId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryPackingSlips_CompanyId_PackingSlipNumber",
                table: "InventoryPackingSlips",
                columns: new[] { "CompanyId", "PackingSlipNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryPackingSlips_CompanyId_SalesOrderId_Status",
                table: "InventoryPackingSlips",
                columns: new[] { "CompanyId", "SalesOrderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryPackingSlips_InventoryPickId",
                table: "InventoryPackingSlips",
                column: "InventoryPickId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryPackingSlips_SalesOrderId",
                table: "InventoryPackingSlips",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryPackingSlips_WarehouseId",
                table: "InventoryPackingSlips",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryPickLines_InventoryItemId",
                table: "InventoryPickLines",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryPickLines_InventoryPickId",
                table: "InventoryPickLines",
                column: "InventoryPickId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryPickLines_SalesOrderLineId",
                table: "InventoryPickLines",
                column: "SalesOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryPicks_BinId",
                table: "InventoryPicks",
                column: "BinId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryPicks_CompanyId_PickNumber",
                table: "InventoryPicks",
                columns: new[] { "CompanyId", "PickNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryPicks_CompanyId_SalesOrderId_Status",
                table: "InventoryPicks",
                columns: new[] { "CompanyId", "SalesOrderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryPicks_SalesOrderId",
                table: "InventoryPicks",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryPicks_WarehouseId",
                table: "InventoryPicks",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderBackorderPromises_CompanyId_SalesOrderId_Status_P~",
                table: "SalesOrderBackorderPromises",
                columns: new[] { "CompanyId", "SalesOrderId", "Status", "PromisedShipOn" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderBackorderPromises_SalesOrderId",
                table: "SalesOrderBackorderPromises",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderBackorderPromises_SalesOrderLineId",
                table: "SalesOrderBackorderPromises",
                column: "SalesOrderLineId");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryShipments_InventoryPackingSlips_InventoryPackingSl~",
                table: "InventoryShipments",
                column: "InventoryPackingSlipId",
                principalTable: "InventoryPackingSlips",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider is not null)
                throw new NotSupportedException("Rolling back pick, pack, and backorder controls could delete fulfillment commitments and provenance and is prohibited. Restore a verified pre-upgrade backup instead.");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryShipments_InventoryPackingSlips_InventoryPackingSl~",
                table: "InventoryShipments");

            migrationBuilder.DropTable(
                name: "InventoryPackingSlipLines");

            migrationBuilder.DropTable(
                name: "SalesOrderBackorderPromises");

            migrationBuilder.DropTable(
                name: "InventoryPackingSlips");

            migrationBuilder.DropTable(
                name: "InventoryPickLines");

            migrationBuilder.DropTable(
                name: "InventoryPicks");

            migrationBuilder.DropIndex(
                name: "IX_InventoryShipments_InventoryPackingSlipId",
                table: "InventoryShipments");

            migrationBuilder.DropColumn(
                name: "InventoryPackingSlipId",
                table: "InventoryShipments");
        }
    }
}
