using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrassLedger.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesOrderTransactionCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TransactionCurrency",
                table: "SalesOrders",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TransactionCurrency",
                table: "SalesOrders");
        }
    }
}
