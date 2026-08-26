using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddFixedAssetDisposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DisposalJournalEntryId",
                table: "AccountingSchedules",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountingSchedules_DisposalJournalEntryId",
                table: "AccountingSchedules",
                column: "DisposalJournalEntryId");

            migrationBuilder.AddForeignKey(
                name: "FK_AccountingSchedules_JournalEntries_DisposalJournalEntryId",
                table: "AccountingSchedules",
                column: "DisposalJournalEntryId",
                principalTable: "JournalEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (!string.IsNullOrWhiteSpace(migrationBuilder.ActiveProvider))
                throw new NotSupportedException("Rolling back fixed-asset disposals could delete disposal links and is prohibited. Restore a verified pre-upgrade backup instead.");

            migrationBuilder.DropForeignKey(
                name: "FK_AccountingSchedules_JournalEntries_DisposalJournalEntryId",
                table: "AccountingSchedules");

            migrationBuilder.DropIndex(
                name: "IX_AccountingSchedules_DisposalJournalEntryId",
                table: "AccountingSchedules");

            migrationBuilder.DropColumn(
                name: "DisposalJournalEntryId",
                table: "AccountingSchedules");
        }
    }
}
