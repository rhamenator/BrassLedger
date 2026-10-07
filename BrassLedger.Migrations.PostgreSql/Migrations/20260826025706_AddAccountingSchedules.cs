using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountingSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountingSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduleNumber = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ScheduleType = table.Column<string>(type: "text", nullable: false),
                    CalculationMethod = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodCount = table.Column<int>(type: "integer", nullable: false),
                    OriginalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ResidualAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AnnualInterestRate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    RelatedAssetAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    BalanceAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpenseAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentBankAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    PreparedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountingSchedules_Accounts_BalanceAccountId",
                        column: x => x.BalanceAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountingSchedules_Accounts_ExpenseAccountId",
                        column: x => x.ExpenseAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountingSchedules_Accounts_RelatedAssetAccountId",
                        column: x => x.RelatedAssetAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountingSchedules_BankAccounts_PaymentBankAccountId",
                        column: x => x.PaymentBankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccountingScheduleInstallments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountingScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    PrincipalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ExpenseAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaymentAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingScheduleInstallments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountingScheduleInstallments_AccountingSchedules_Accounti~",
                        column: x => x.AccountingScheduleId,
                        principalTable: "AccountingSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccountingScheduleInstallments_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountingScheduleInstallments_AccountingScheduleId_Sequence",
                table: "AccountingScheduleInstallments",
                columns: new[] { "AccountingScheduleId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountingScheduleInstallments_JournalEntryId",
                table: "AccountingScheduleInstallments",
                column: "JournalEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountingSchedules_BalanceAccountId",
                table: "AccountingSchedules",
                column: "BalanceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountingSchedules_CompanyId_ScheduleNumber",
                table: "AccountingSchedules",
                columns: new[] { "CompanyId", "ScheduleNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountingSchedules_ExpenseAccountId",
                table: "AccountingSchedules",
                column: "ExpenseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountingSchedules_PaymentBankAccountId",
                table: "AccountingSchedules",
                column: "PaymentBankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountingSchedules_RelatedAssetAccountId",
                table: "AccountingSchedules",
                column: "RelatedAssetAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (!string.IsNullOrWhiteSpace(migrationBuilder.ActiveProvider))
                throw new NotSupportedException("Rolling back accounting schedules could delete schedule and installment history and is prohibited. Restore a verified pre-upgrade backup instead.");

            migrationBuilder.DropTable(
                name: "AccountingScheduleInstallments");

            migrationBuilder.DropTable(
                name: "AccountingSchedules");
        }
    }
}
