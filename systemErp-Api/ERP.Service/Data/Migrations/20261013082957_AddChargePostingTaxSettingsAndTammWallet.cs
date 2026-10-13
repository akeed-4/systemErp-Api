using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChargePostingTaxSettingsAndTammWallet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDisbursement",
                table: "ExpenseType",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AccruedChargesAccount",
                table: "CarShowroomSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AddOnCostAccount",
                table: "CarShowroomSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AddOnRevenueAccount",
                table: "CarShowroomSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CarCogsAccount",
                table: "CarShowroomSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CarSalesRevenueAccount",
                table: "CarShowroomSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CustomerDepositsAccount",
                table: "CarShowroomSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CustomerRechargeAccount",
                table: "CarShowroomSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MarginBasis",
                table: "CarShowroomSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PrepaidExpensesAccount",
                table: "CarShowroomSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TammWalletAccount",
                table: "CarShowroomSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VehicleExpenseAccount",
                table: "CarShowroomSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VehicleInventoryAccount",
                table: "CarShowroomSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VehiclesInTransitAccount",
                table: "CarShowroomSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "MarginBaseCost",
                table: "CarSalesContract",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentAccountCode",
                table: "AdditionalChargeLine",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PostedInTransit",
                table: "AdditionalChargeAllocation",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ReclassedToInventory",
                table: "AdditionalChargeAllocation",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "TammWalletTransaction",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TreasuryAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TammOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StatementBalance = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    BookBalance = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Difference = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TammWalletTransaction", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TammWalletTransaction_TenantId",
                table: "TammWalletTransaction",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TammWalletTransaction");

            migrationBuilder.DropColumn(
                name: "IsDisbursement",
                table: "ExpenseType");

            migrationBuilder.DropColumn(
                name: "AccruedChargesAccount",
                table: "CarShowroomSettings");

            migrationBuilder.DropColumn(
                name: "AddOnCostAccount",
                table: "CarShowroomSettings");

            migrationBuilder.DropColumn(
                name: "AddOnRevenueAccount",
                table: "CarShowroomSettings");

            migrationBuilder.DropColumn(
                name: "CarCogsAccount",
                table: "CarShowroomSettings");

            migrationBuilder.DropColumn(
                name: "CarSalesRevenueAccount",
                table: "CarShowroomSettings");

            migrationBuilder.DropColumn(
                name: "CustomerDepositsAccount",
                table: "CarShowroomSettings");

            migrationBuilder.DropColumn(
                name: "CustomerRechargeAccount",
                table: "CarShowroomSettings");

            migrationBuilder.DropColumn(
                name: "MarginBasis",
                table: "CarShowroomSettings");

            migrationBuilder.DropColumn(
                name: "PrepaidExpensesAccount",
                table: "CarShowroomSettings");

            migrationBuilder.DropColumn(
                name: "TammWalletAccount",
                table: "CarShowroomSettings");

            migrationBuilder.DropColumn(
                name: "VehicleExpenseAccount",
                table: "CarShowroomSettings");

            migrationBuilder.DropColumn(
                name: "VehicleInventoryAccount",
                table: "CarShowroomSettings");

            migrationBuilder.DropColumn(
                name: "VehiclesInTransitAccount",
                table: "CarShowroomSettings");

            migrationBuilder.DropColumn(
                name: "MarginBaseCost",
                table: "CarSalesContract");

            migrationBuilder.DropColumn(
                name: "PaymentAccountCode",
                table: "AdditionalChargeLine");

            migrationBuilder.DropColumn(
                name: "PostedInTransit",
                table: "AdditionalChargeAllocation");

            migrationBuilder.DropColumn(
                name: "ReclassedToInventory",
                table: "AdditionalChargeAllocation");
        }
    }
}
