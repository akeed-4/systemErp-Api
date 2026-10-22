using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceVatTreatment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VatTreatment",
                table: "SalesReturn",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "Standard");

            migrationBuilder.AddColumn<string>(
                name: "VatTreatmentReasonCode",
                table: "SalesReturn",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VatTreatment",
                table: "SalesInvoice",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "Standard");

            migrationBuilder.AddColumn<string>(
                name: "VatTreatmentReasonCode",
                table: "SalesInvoice",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VatTreatment",
                table: "PurchaseReturn",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "Standard");

            migrationBuilder.AddColumn<string>(
                name: "VatTreatmentReasonCode",
                table: "PurchaseReturn",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VatTreatment",
                table: "PurchaseInvoice",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "Standard");

            migrationBuilder.AddColumn<string>(
                name: "VatTreatmentReasonCode",
                table: "PurchaseInvoice",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VatTreatment",
                table: "Invoice",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "Standard");

            migrationBuilder.AddColumn<string>(
                name: "VatTreatmentReasonCode",
                table: "Invoice",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VatTreatment",
                table: "SalesReturn");

            migrationBuilder.DropColumn(
                name: "VatTreatmentReasonCode",
                table: "SalesReturn");

            migrationBuilder.DropColumn(
                name: "VatTreatment",
                table: "SalesInvoice");

            migrationBuilder.DropColumn(
                name: "VatTreatmentReasonCode",
                table: "SalesInvoice");

            migrationBuilder.DropColumn(
                name: "VatTreatment",
                table: "PurchaseReturn");

            migrationBuilder.DropColumn(
                name: "VatTreatmentReasonCode",
                table: "PurchaseReturn");

            migrationBuilder.DropColumn(
                name: "VatTreatment",
                table: "PurchaseInvoice");

            migrationBuilder.DropColumn(
                name: "VatTreatmentReasonCode",
                table: "PurchaseInvoice");

            migrationBuilder.DropColumn(
                name: "VatTreatment",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "VatTreatmentReasonCode",
                table: "Invoice");
        }
    }
}
