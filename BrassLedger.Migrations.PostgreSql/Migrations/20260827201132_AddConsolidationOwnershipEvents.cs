using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddConsolidationOwnershipEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsolidationOwnershipEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsolidationGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectCompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    Reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FrameworkCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FrameworkEdition = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    ContentJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PreparedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RejectedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RejectedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PostedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    PostedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecisionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ReversalOfEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversedByEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversalReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsolidationOwnershipEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsolidationOwnershipEvents_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsolidationOwnershipEvents_Companies_SubjectCompanyId",
                        column: x => x.SubjectCompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsolidationOwnershipEvents_ConsolidationGroups_Consolidat~",
                        column: x => x.ConsolidationGroupId,
                        principalTable: "ConsolidationGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsolidationOwnershipEvents_ConsolidationOwnershipEvents_R~",
                        column: x => x.ReversalOfEventId,
                        principalTable: "ConsolidationOwnershipEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationOwnershipEvents_CompanyId_Status_EventDate",
                table: "ConsolidationOwnershipEvents",
                columns: new[] { "CompanyId", "Status", "EventDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationOwnershipEvents_ConsolidationGroupId_Reference",
                table: "ConsolidationOwnershipEvents",
                columns: new[] { "ConsolidationGroupId", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationOwnershipEvents_ConsolidationGroupId_SubjectCo~",
                table: "ConsolidationOwnershipEvents",
                columns: new[] { "ConsolidationGroupId", "SubjectCompanyId", "EventDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationOwnershipEvents_ReversalOfEventId",
                table: "ConsolidationOwnershipEvents",
                column: "ReversalOfEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidationOwnershipEvents_SubjectCompanyId",
                table: "ConsolidationOwnershipEvents",
                column: "SubjectCompanyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Downgrade is prohibited because it could delete reviewed consolidation acquisition, disposal, ownership-change, and attribution schedules. Restore a verified pre-upgrade backup instead.");
        }
    }
}
