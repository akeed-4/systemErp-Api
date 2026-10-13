using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLandedCostVouchersAndInTransit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProcurementOrderItemId",
                table: "Vehicle",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VolumeCbm",
                table: "Vehicle",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WeightKg",
                table: "Vehicle",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LandedCostVoucher",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoucherNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SupplierInvoiceNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CurrencyCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ProcurementOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProcurementOrderNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Subtotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    GrandTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BaseCostTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LandedCostVoucher", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LandedCostVoucher_CarProcurementOrder_ProcurementOrderId",
                        column: x => x.ProcurementOrderId,
                        principalTable: "CarProcurementOrder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LandedCostVoucher_Supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LandedCostVoucherLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoucherId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ExpenseTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpenseTypeCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpenseTypeNameAr = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BaseAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CostAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Treatment = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AllocationMethod = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReplacesLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReplacedLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReplacedLineStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EstimatedAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Variance = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    ChargeLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LandedCostVoucherLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LandedCostVoucherLine_ExpenseType_ExpenseTypeId",
                        column: x => x.ExpenseTypeId,
                        principalTable: "ExpenseType",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LandedCostVoucherLine_LandedCostVoucher_VoucherId",
                        column: x => x.VoucherId,
                        principalTable: "LandedCostVoucher",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LandedCostVoucherAllocation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LandedCostVoucherAllocation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LandedCostVoucherAllocation_LandedCostVoucherLine_LineId",
                        column: x => x.LineId,
                        principalTable: "LandedCostVoucherLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LandedCostVoucherAllocation_Vehicle_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicle",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostVoucher_ProcurementOrderId",
                table: "LandedCostVoucher",
                column: "ProcurementOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostVoucher_SupplierId",
                table: "LandedCostVoucher",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostVoucher_TenantId",
                table: "LandedCostVoucher",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostVoucher_TenantId_VoucherNumber",
                table: "LandedCostVoucher",
                columns: new[] { "TenantId", "VoucherNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostVoucherAllocation_LineId",
                table: "LandedCostVoucherAllocation",
                column: "LineId");

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostVoucherAllocation_TenantId",
                table: "LandedCostVoucherAllocation",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostVoucherAllocation_VehicleId",
                table: "LandedCostVoucherAllocation",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostVoucherLine_ExpenseTypeId",
                table: "LandedCostVoucherLine",
                column: "ExpenseTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostVoucherLine_TenantId",
                table: "LandedCostVoucherLine",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostVoucherLine_VoucherId",
                table: "LandedCostVoucherLine",
                column: "VoucherId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LandedCostVoucherAllocation");

            migrationBuilder.DropTable(
                name: "LandedCostVoucherLine");

            migrationBuilder.DropTable(
                name: "LandedCostVoucher");

            migrationBuilder.DropColumn(
                name: "ProcurementOrderItemId",
                table: "Vehicle");

            migrationBuilder.DropColumn(
                name: "VolumeCbm",
                table: "Vehicle");

            migrationBuilder.DropColumn(
                name: "WeightKg",
                table: "Vehicle");
        }
    }
}
