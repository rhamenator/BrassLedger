using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackingDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClassId",
                table: "JournalEntryLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "JournalEntryLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TrackingDimensionValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentTrackingDimensionValueId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DimensionType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EffectiveThrough = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackingDimensionValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackingDimensionValues_TrackingDimensionValues_ParentTrackingDimensionValueId",
                        column: x => x.ParentTrackingDimensionValueId,
                        principalTable: "TrackingDimensionValues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_ClassId",
                table: "JournalEntryLines",
                column: "ClassId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_DepartmentId",
                table: "JournalEntryLines",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackingDimensionValues_CompanyId_DimensionType_Code",
                table: "TrackingDimensionValues",
                columns: new[] { "CompanyId", "DimensionType", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackingDimensionValues_CompanyId_DimensionType_ParentTrackingDimensionValueId",
                table: "TrackingDimensionValues",
                columns: new[] { "CompanyId", "DimensionType", "ParentTrackingDimensionValueId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackingDimensionValues_ParentTrackingDimensionValueId",
                table: "TrackingDimensionValues",
                column: "ParentTrackingDimensionValueId");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_TrackingDimensionValues_ClassId",
                table: "JournalEntryLines",
                column: "ClassId",
                principalTable: "TrackingDimensionValues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_TrackingDimensionValues_DepartmentId",
                table: "JournalEntryLines",
                column: "DepartmentId",
                principalTable: "TrackingDimensionValues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Downgrade is prohibited because it could delete retained department and class masters, hierarchy history, effective dates, and journal attribution. Restore a verified pre-upgrade backup instead.");
        }
    }
}
