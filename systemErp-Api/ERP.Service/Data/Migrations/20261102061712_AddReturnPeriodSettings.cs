using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnPeriodSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PurchaseReturnPeriodDays",
                table: "AccountingSettings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SalesReturnPeriodDays",
                table: "AccountingSettings",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PurchaseReturnPeriodDays",
                table: "AccountingSettings");

            migrationBuilder.DropColumn(
                name: "SalesReturnPeriodDays",
                table: "AccountingSettings");
        }
    }
}
