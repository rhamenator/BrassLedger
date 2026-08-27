using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectPhaseCostCodeBudgets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectCostCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectCostCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectPhases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectJobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentProjectPhaseId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EndsOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectPhases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectPhases_ProjectJobs_ProjectJobId",
                        column: x => x.ProjectJobId,
                        principalTable: "ProjectJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectPhases_ProjectPhases_ParentProjectPhaseId",
                        column: x => x.ParentProjectPhaseId,
                        principalTable: "ProjectPhases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectBudgetAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectJobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectPhaseId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProjectCostCodeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PeriodStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    BudgetAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ForecastAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectBudgetAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectBudgetAllocations_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectBudgetAllocations_ProjectCostCodes_ProjectCostCodeId",
                        column: x => x.ProjectCostCodeId,
                        principalTable: "ProjectCostCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectBudgetAllocations_ProjectJobs_ProjectJobId",
                        column: x => x.ProjectJobId,
                        principalTable: "ProjectJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectBudgetAllocations_ProjectPhases_ProjectPhaseId",
                        column: x => x.ProjectPhaseId,
                        principalTable: "ProjectPhases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectBudgetAllocations_AccountId",
                table: "ProjectBudgetAllocations",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectBudgetAllocations_CompanyId_ProjectJobId_PeriodStart_PeriodEnd",
                table: "ProjectBudgetAllocations",
                columns: new[] { "CompanyId", "ProjectJobId", "PeriodStart", "PeriodEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectBudgetAllocations_ProjectCostCodeId",
                table: "ProjectBudgetAllocations",
                column: "ProjectCostCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectBudgetAllocations_ProjectJobId_ProjectPhaseId_ProjectCostCodeId_AccountId_PeriodStart_PeriodEnd",
                table: "ProjectBudgetAllocations",
                columns: new[] { "ProjectJobId", "ProjectPhaseId", "ProjectCostCodeId", "AccountId", "PeriodStart", "PeriodEnd" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectBudgetAllocations_ProjectPhaseId",
                table: "ProjectBudgetAllocations",
                column: "ProjectPhaseId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCostCodes_CompanyId_Code",
                table: "ProjectCostCodes",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPhases_CompanyId_ProjectJobId_Code",
                table: "ProjectPhases",
                columns: new[] { "CompanyId", "ProjectJobId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPhases_CompanyId_ProjectJobId_ParentProjectPhaseId",
                table: "ProjectPhases",
                columns: new[] { "CompanyId", "ProjectJobId", "ParentProjectPhaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPhases_ParentProjectPhaseId",
                table: "ProjectPhases",
                column: "ParentProjectPhaseId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPhases_ProjectJobId",
                table: "ProjectPhases",
                column: "ProjectJobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Downgrade is prohibited because it could delete retained project phase, cost-code, budget, forecast, and audit relationships. Restore a verified pre-upgrade backup instead.");
        }
    }
}
