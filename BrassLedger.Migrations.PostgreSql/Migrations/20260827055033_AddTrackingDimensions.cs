using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.PostgreSql.Migrations
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
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "JournalEntryLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TrackingDimensionValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentTrackingDimensionValueId = table.Column<Guid>(type: "uuid", nullable: true),
                    DimensionType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectiveThrough = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackingDimensionValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackingDimensionValues_TrackingDimensionValues_ParentTrack~",
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
                name: "IX_TrackingDimensionValues_CompanyId_DimensionType_ParentTrack~",
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
