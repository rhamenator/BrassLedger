using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewedIntercompanyMatching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsolidationIntercompanyMatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsolidationGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerCompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuyerCompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesInvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorBillId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchReference = table.Column<string>(type: "text", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SellerBalanceDue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BuyerBalanceDue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    DiscoveredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewReason = table.Column<string>(type: "text", nullable: false),
                    ConsolidationAdjustmentBatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsolidationIntercompanyMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsolidationIntercompanyMatches_Companies_BuyerCompanyId",
                        column: x => x.BuyerCompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsolidationIntercompanyMatches_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsolidationIntercompanyMatches_Companies_SellerCompanyId",
                        column: x => x.SellerCompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsolidationIntercompanyMatches_ConsolidationAdjustmentBat~",
                        column: x => x.ConsolidationAdjustmentBatchId,
                        principalTable: "ConsolidationAdjustmentBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsolidationIntercompanyMatches_ConsolidationGroups_Consol~",
                        column: x => x.ConsolidationGroupId,
                        principalTable: "ConsolidationGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsolidationIntercompanyMatches_SalesInvoices_SalesInvoice~",
                        column: x => x.SalesInvoiceId,
                        principalTable: "SalesInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsolidationIntercompanyMatches_VendorBills_VendorBillId",
                        column: x => x.VendorBillId,
                        principalTable: "VendorBills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConsolidationTradingPartners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsolidationGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberCompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CounterpartyCompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveThrough = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsolidationTradingPartners", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsolidationTradingPartners_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsolidationTradingPartners_Companies_CounterpartyCompanyId",
                        column: x => x.CounterpartyCompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsolidationTradingPartners_Companies_MemberCompanyId",
                        column: x => x.MemberCompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsolidationTradingPartners_ConsolidationGroups_Consolidat~",
                        column: x => x.ConsolidationGroupId,
                        principalTable: "ConsolidationGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsolidationTradingPartners_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsolidationTradingPartners_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationIntercompanyMatches_BuyerCompanyId",
                table: "ConsolidationIntercompanyMatches",
                column: "BuyerCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationIntercompanyMatches_CompanyId",
                table: "ConsolidationIntercompanyMatches",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationIntercompanyMatches_ConsolidationAdjustmentBat~",
                table: "ConsolidationIntercompanyMatches",
                column: "ConsolidationAdjustmentBatchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationIntercompanyMatches_ConsolidationGroupId_Sales~",
                table: "ConsolidationIntercompanyMatches",
                columns: new[] { "ConsolidationGroupId", "SalesInvoiceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationIntercompanyMatches_ConsolidationGroupId_Vendo~",
                table: "ConsolidationIntercompanyMatches",
                columns: new[] { "ConsolidationGroupId", "VendorBillId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationIntercompanyMatches_SalesInvoiceId",
                table: "ConsolidationIntercompanyMatches",
                column: "SalesInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationIntercompanyMatches_SellerCompanyId",
                table: "ConsolidationIntercompanyMatches",
                column: "SellerCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationIntercompanyMatches_VendorBillId",
                table: "ConsolidationIntercompanyMatches",
                column: "VendorBillId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationTradingPartners_CompanyId",
                table: "ConsolidationTradingPartners",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationTradingPartners_ConsolidationGroupId_MemberCo~1",
                table: "ConsolidationTradingPartners",
                columns: new[] { "ConsolidationGroupId", "MemberCompanyId", "VendorId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationTradingPartners_ConsolidationGroupId_MemberCom~",
                table: "ConsolidationTradingPartners",
                columns: new[] { "ConsolidationGroupId", "MemberCompanyId", "CustomerId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationTradingPartners_CounterpartyCompanyId",
                table: "ConsolidationTradingPartners",
                column: "CounterpartyCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationTradingPartners_CustomerId",
                table: "ConsolidationTradingPartners",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationTradingPartners_MemberCompanyId",
                table: "ConsolidationTradingPartners",
                column: "MemberCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationTradingPartners_VendorId",
                table: "ConsolidationTradingPartners",
                column: "VendorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Downgrade is prohibited because it could delete effective-dated trading-partner links, reviewed match decisions, adjustment links, and audit provenance. Restore a verified pre-upgrade backup instead.");
        }
    }
}
