using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceJournalCostCenters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "JournalCostCentersJson",
                table: "SalesReturn",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JournalCostCentersJson",
                table: "SalesInvoice",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JournalCostCentersJson",
                table: "PurchaseReturn",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JournalCostCentersJson",
                table: "PurchaseInvoice",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JournalCostCentersJson",
                table: "Invoice",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "JournalCostCentersJson",
                table: "SalesReturn");

            migrationBuilder.DropColumn(
                name: "JournalCostCentersJson",
                table: "SalesInvoice");

            migrationBuilder.DropColumn(
                name: "JournalCostCentersJson",
                table: "PurchaseReturn");

            migrationBuilder.DropColumn(
                name: "JournalCostCentersJson",
                table: "PurchaseInvoice");

            migrationBuilder.DropColumn(
                name: "JournalCostCentersJson",
                table: "Invoice");
        }
    }
}
