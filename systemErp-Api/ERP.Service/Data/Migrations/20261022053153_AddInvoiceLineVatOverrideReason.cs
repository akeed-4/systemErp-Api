using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceLineVatOverrideReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VatOverrideReason",
                table: "SalesReturnItem",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VatOverrideReason",
                table: "SalesInvoiceItem",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VatOverrideReason",
                table: "PurchaseReturnItem",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VatOverrideReason",
                table: "PurchaseInvoiceItem",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VatOverrideReason",
                table: "InvoiceItem",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VatOverrideReason",
                table: "SalesReturnItem");

            migrationBuilder.DropColumn(
                name: "VatOverrideReason",
                table: "SalesInvoiceItem");

            migrationBuilder.DropColumn(
                name: "VatOverrideReason",
                table: "PurchaseReturnItem");

            migrationBuilder.DropColumn(
                name: "VatOverrideReason",
                table: "PurchaseInvoiceItem");

            migrationBuilder.DropColumn(
                name: "VatOverrideReason",
                table: "InvoiceItem");
        }
    }
}
