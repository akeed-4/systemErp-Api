using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExciseTaxForLocalSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ExciseRetailPrice",
                table: "SalesReturnItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseStandardPrice",
                table: "SalesReturnItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseTaxAmount",
                table: "SalesReturnItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseTaxBaseAmount",
                table: "SalesReturnItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseTaxRate",
                table: "SalesReturnItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseTaxTotal",
                table: "SalesReturn",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseRetailPrice",
                table: "SalesInvoiceItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseStandardPrice",
                table: "SalesInvoiceItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseTaxAmount",
                table: "SalesInvoiceItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseTaxBaseAmount",
                table: "SalesInvoiceItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseTaxRate",
                table: "SalesInvoiceItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseTaxTotal",
                table: "SalesInvoice",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseRetailPrice",
                table: "PurchaseReturnItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseStandardPrice",
                table: "PurchaseReturnItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseTaxAmount",
                table: "PurchaseReturnItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseTaxBaseAmount",
                table: "PurchaseReturnItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseTaxRate",
                table: "PurchaseReturnItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseTaxTotal",
                table: "PurchaseReturn",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseRetailPrice",
                table: "PurchaseInvoiceItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseStandardPrice",
                table: "PurchaseInvoiceItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseTaxAmount",
                table: "PurchaseInvoiceItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseTaxBaseAmount",
                table: "PurchaseInvoiceItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseTaxRate",
                table: "PurchaseInvoiceItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseTaxTotal",
                table: "PurchaseInvoice",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseRetailPrice",
                table: "Product",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseStandardPrice",
                table: "Product",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseTaxRate",
                table: "Product",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseRetailPrice",
                table: "PosTransactionItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseStandardPrice",
                table: "PosTransactionItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseTaxAmount",
                table: "PosTransactionItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseTaxBaseAmount",
                table: "PosTransactionItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseTaxRate",
                table: "PosTransactionItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseTaxAmount",
                table: "PosTransaction",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalExciseTax",
                table: "PosShift",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseRetailPrice",
                table: "PosSalesReturnItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseStandardPrice",
                table: "PosSalesReturnItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseTaxAmount",
                table: "PosSalesReturnItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseTaxBaseAmount",
                table: "PosSalesReturnItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseTaxRate",
                table: "PosSalesReturnItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseTaxAmount",
                table: "PosSalesReturn",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseRetailPrice",
                table: "InvoiceItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseStandardPrice",
                table: "InvoiceItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseTaxAmount",
                table: "InvoiceItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseTaxBaseAmount",
                table: "InvoiceItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseTaxRate",
                table: "InvoiceItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExciseTaxTotal",
                table: "Invoice",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "OutputExciseAccountCode",
                table: "AccountingSettings",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExciseRetailPrice",
                table: "SalesReturnItem");

            migrationBuilder.DropColumn(
                name: "ExciseStandardPrice",
                table: "SalesReturnItem");

            migrationBuilder.DropColumn(
                name: "ExciseTaxAmount",
                table: "SalesReturnItem");

            migrationBuilder.DropColumn(
                name: "ExciseTaxBaseAmount",
                table: "SalesReturnItem");

            migrationBuilder.DropColumn(
                name: "ExciseTaxRate",
                table: "SalesReturnItem");

            migrationBuilder.DropColumn(
                name: "ExciseTaxTotal",
                table: "SalesReturn");

            migrationBuilder.DropColumn(
                name: "ExciseRetailPrice",
                table: "SalesInvoiceItem");

            migrationBuilder.DropColumn(
                name: "ExciseStandardPrice",
                table: "SalesInvoiceItem");

            migrationBuilder.DropColumn(
                name: "ExciseTaxAmount",
                table: "SalesInvoiceItem");

            migrationBuilder.DropColumn(
                name: "ExciseTaxBaseAmount",
                table: "SalesInvoiceItem");

            migrationBuilder.DropColumn(
                name: "ExciseTaxRate",
                table: "SalesInvoiceItem");

            migrationBuilder.DropColumn(
                name: "ExciseTaxTotal",
                table: "SalesInvoice");

            migrationBuilder.DropColumn(
                name: "ExciseRetailPrice",
                table: "PurchaseReturnItem");

            migrationBuilder.DropColumn(
                name: "ExciseStandardPrice",
                table: "PurchaseReturnItem");

            migrationBuilder.DropColumn(
                name: "ExciseTaxAmount",
                table: "PurchaseReturnItem");

            migrationBuilder.DropColumn(
                name: "ExciseTaxBaseAmount",
                table: "PurchaseReturnItem");

            migrationBuilder.DropColumn(
                name: "ExciseTaxRate",
                table: "PurchaseReturnItem");

            migrationBuilder.DropColumn(
                name: "ExciseTaxTotal",
                table: "PurchaseReturn");

            migrationBuilder.DropColumn(
                name: "ExciseRetailPrice",
                table: "PurchaseInvoiceItem");

            migrationBuilder.DropColumn(
                name: "ExciseStandardPrice",
                table: "PurchaseInvoiceItem");

            migrationBuilder.DropColumn(
                name: "ExciseTaxAmount",
                table: "PurchaseInvoiceItem");

            migrationBuilder.DropColumn(
                name: "ExciseTaxBaseAmount",
                table: "PurchaseInvoiceItem");

            migrationBuilder.DropColumn(
                name: "ExciseTaxRate",
                table: "PurchaseInvoiceItem");

            migrationBuilder.DropColumn(
                name: "ExciseTaxTotal",
                table: "PurchaseInvoice");

            migrationBuilder.DropColumn(
                name: "ExciseRetailPrice",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "ExciseStandardPrice",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "ExciseTaxRate",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "ExciseRetailPrice",
                table: "PosTransactionItem");

            migrationBuilder.DropColumn(
                name: "ExciseStandardPrice",
                table: "PosTransactionItem");

            migrationBuilder.DropColumn(
                name: "ExciseTaxAmount",
                table: "PosTransactionItem");

            migrationBuilder.DropColumn(
                name: "ExciseTaxBaseAmount",
                table: "PosTransactionItem");

            migrationBuilder.DropColumn(
                name: "ExciseTaxRate",
                table: "PosTransactionItem");

            migrationBuilder.DropColumn(
                name: "ExciseTaxAmount",
                table: "PosTransaction");

            migrationBuilder.DropColumn(
                name: "TotalExciseTax",
                table: "PosShift");

            migrationBuilder.DropColumn(
                name: "ExciseRetailPrice",
                table: "PosSalesReturnItem");

            migrationBuilder.DropColumn(
                name: "ExciseStandardPrice",
                table: "PosSalesReturnItem");

            migrationBuilder.DropColumn(
                name: "ExciseTaxAmount",
                table: "PosSalesReturnItem");

            migrationBuilder.DropColumn(
                name: "ExciseTaxBaseAmount",
                table: "PosSalesReturnItem");

            migrationBuilder.DropColumn(
                name: "ExciseTaxRate",
                table: "PosSalesReturnItem");

            migrationBuilder.DropColumn(
                name: "ExciseTaxAmount",
                table: "PosSalesReturn");

            migrationBuilder.DropColumn(
                name: "ExciseRetailPrice",
                table: "InvoiceItem");

            migrationBuilder.DropColumn(
                name: "ExciseStandardPrice",
                table: "InvoiceItem");

            migrationBuilder.DropColumn(
                name: "ExciseTaxAmount",
                table: "InvoiceItem");

            migrationBuilder.DropColumn(
                name: "ExciseTaxBaseAmount",
                table: "InvoiceItem");

            migrationBuilder.DropColumn(
                name: "ExciseTaxRate",
                table: "InvoiceItem");

            migrationBuilder.DropColumn(
                name: "ExciseTaxTotal",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "OutputExciseAccountCode",
                table: "AccountingSettings");
        }
    }
}
