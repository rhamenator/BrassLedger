using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddControlledProjectChangeOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectChangeOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectJobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChangeOrderNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    RequestedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EffectiveOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ContractAmountChange = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    BudgetAmountChange = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ContractAmountBefore = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    ContractAmountAfter = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    BudgetAmountBefore = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    BudgetAmountAfter = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PreparedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    SubmittedProjectConcurrencyToken = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DecisionReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    CancelledByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CancellationReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectChangeOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectChangeOrders_ProjectJobs_ProjectJobId",
                        column: x => x.ProjectJobId,
                        principalTable: "ProjectJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectChangeOrders_CompanyId_ProjectJobId_ChangeOrderNumber",
                table: "ProjectChangeOrders",
                columns: new[] { "CompanyId", "ProjectJobId", "ChangeOrderNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectChangeOrders_CompanyId_Status_EffectiveOn",
                table: "ProjectChangeOrders",
                columns: new[] { "CompanyId", "Status", "EffectiveOn" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectChangeOrders_ProjectJobId",
                table: "ProjectChangeOrders",
                column: "ProjectJobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Downgrade is prohibited because it could delete project change-order authorization and contract/budget history. Restore a verified pre-upgrade backup instead.");
        }
    }
}
