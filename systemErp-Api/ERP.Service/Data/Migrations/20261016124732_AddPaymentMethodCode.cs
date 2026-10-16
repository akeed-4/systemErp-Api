using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentMethodCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaymentMethodCode",
                table: "VoucherPaymentSplit",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethodCode",
                table: "InvoicePaymentSplit",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethodCode",
                table: "Invoice",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentMethodCode",
                table: "VoucherPaymentSplit");

            migrationBuilder.DropColumn(
                name: "PaymentMethodCode",
                table: "InvoicePaymentSplit");

            migrationBuilder.DropColumn(
                name: "PaymentMethodCode",
                table: "Invoice");
        }
    }
}
