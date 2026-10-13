using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSpecialCasesAndPrepaidExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReceivedAt",
                table: "Vehicle",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DepositCancellationPolicy",
                table: "CarShowroomSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ForfeitedDepositsAccount",
                table: "CarShowroomSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PurchaseCancellationLossAccount",
                table: "CarShowroomSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "RefundCustomerChargesOnReturn",
                table: "CarShowroomSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SupplierClaimsAccount",
                table: "CarShowroomSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "CancelledChargesAmount",
                table: "CarProcurementOrder",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledChargesEntryId",
                table: "CarProcurementOrder",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledChargesSettlement",
                table: "CarProcurementOrder",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedCostAmount",
                table: "AdditionalChargeLine",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PostedToCogs",
                table: "AdditionalChargeAllocation",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "PrepaidExpense",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ExpenseTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpenseTypeNameAr = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Months = table.Column<int>(type: "int", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    AmortizedAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PaymentAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrepaidExpense", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrepaidExpense_ExpenseType_ExpenseTypeId",
                        column: x => x.ExpenseTypeId,
                        principalTable: "ExpenseType",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrepaidExpense_Supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VehicleCostAdjustment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdjustmentNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChargeLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleCostAdjustment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehicleCostAdjustment_Vehicle_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicle",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PrepaidExpenseAmortization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrepaidExpenseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Period = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrepaidExpenseAmortization", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrepaidExpenseAmortization_PrepaidExpense_PrepaidExpenseId",
                        column: x => x.PrepaidExpenseId,
                        principalTable: "PrepaidExpense",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrepaidExpense_ExpenseTypeId",
                table: "PrepaidExpense",
                column: "ExpenseTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PrepaidExpense_SupplierId",
                table: "PrepaidExpense",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_PrepaidExpense_TenantId",
                table: "PrepaidExpense",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PrepaidExpense_TenantId_Number",
                table: "PrepaidExpense",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrepaidExpenseAmortization_PrepaidExpenseId",
                table: "PrepaidExpenseAmortization",
                column: "PrepaidExpenseId");

            migrationBuilder.CreateIndex(
                name: "IX_PrepaidExpenseAmortization_TenantId",
                table: "PrepaidExpenseAmortization",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PrepaidExpenseAmortization_TenantId_PrepaidExpenseId_Period",
                table: "PrepaidExpenseAmortization",
                columns: new[] { "TenantId", "PrepaidExpenseId", "Period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleCostAdjustment_TenantId",
                table: "VehicleCostAdjustment",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleCostAdjustment_TenantId_AdjustmentNumber",
                table: "VehicleCostAdjustment",
                columns: new[] { "TenantId", "AdjustmentNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleCostAdjustment_VehicleId",
                table: "VehicleCostAdjustment",
                column: "VehicleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrepaidExpenseAmortization");

            migrationBuilder.DropTable(
                name: "VehicleCostAdjustment");

            migrationBuilder.DropTable(
                name: "PrepaidExpense");

            migrationBuilder.DropColumn(
                name: "ReceivedAt",
                table: "Vehicle");

            migrationBuilder.DropColumn(
                name: "DepositCancellationPolicy",
                table: "CarShowroomSettings");

            migrationBuilder.DropColumn(
                name: "ForfeitedDepositsAccount",
                table: "CarShowroomSettings");

            migrationBuilder.DropColumn(
                name: "PurchaseCancellationLossAccount",
                table: "CarShowroomSettings");

            migrationBuilder.DropColumn(
                name: "RefundCustomerChargesOnReturn",
                table: "CarShowroomSettings");

            migrationBuilder.DropColumn(
                name: "SupplierClaimsAccount",
                table: "CarShowroomSettings");

            migrationBuilder.DropColumn(
                name: "CancelledChargesAmount",
                table: "CarProcurementOrder");

            migrationBuilder.DropColumn(
                name: "CancelledChargesEntryId",
                table: "CarProcurementOrder");

            migrationBuilder.DropColumn(
                name: "CancelledChargesSettlement",
                table: "CarProcurementOrder");

            migrationBuilder.DropColumn(
                name: "EstimatedCostAmount",
                table: "AdditionalChargeLine");

            migrationBuilder.DropColumn(
                name: "PostedToCogs",
                table: "AdditionalChargeAllocation");
        }
    }
}
