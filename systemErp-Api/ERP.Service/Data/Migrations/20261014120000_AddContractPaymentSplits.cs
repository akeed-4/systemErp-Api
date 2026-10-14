using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ERP.Service.Data;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261014120000_AddContractPaymentSplits")]
    public partial class AddContractPaymentSplits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaymentSplitsJson",
                table: "CarSalesContract",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentSplitsJson",
                table: "CarSalesContract");
        }
    }
}
