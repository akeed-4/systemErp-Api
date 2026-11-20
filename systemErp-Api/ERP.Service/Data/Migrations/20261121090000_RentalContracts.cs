using System;
using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// عقود تأجير المعدات للعملاء (R2): العقد وأسطره وسجل تغييراته (التمديد والتجديد)، وربط الحجز بالعقد المحوَّل إليه (مرجع بلا قيد).
    /// جداول جديدة وعمود فارغ؛ لا يتغير شيء قائم.
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261121090000_RentalContracts")]
    public partial class RentalContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(name: "ContractId", table: "RentalBooking", type: "uniqueidentifier", nullable: true);

            migrationBuilder.CreateTable(
                name: "RentalContract",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ParentContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Term = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    BillingCycle = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    BillInAdvance = table.Column<bool>(type: "bit", nullable: false),
                    DepositRequired = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Terms = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActivatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActivatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RentalContract", x => x.Id);
                    table.ForeignKey(name: "FK_RentalContract_Customer_CustomerId", column: x => x.CustomerId, principalTable: "Customer",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_RentalContract_RentalBooking_BookingId", column: x => x.BookingId, principalTable: "RentalBooking",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_RentalContract_RentalContract_ParentContractId", column: x => x.ParentContractId, principalTable: "RentalContract",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_RentalContract_Warehouse_WarehouseId", column: x => x.WarehouseId, principalTable: "Warehouse",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RentalContractChange",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OldEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NewEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NewContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RentalContractChange", x => x.Id);
                    table.ForeignKey(name: "FK_RentalContractChange_RentalContract_ContractId", column: x => x.ContractId, principalTable: "RentalContract",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_RentalContractChange_RentalContract_NewContractId", column: x => x.NewContractId, principalTable: "RentalContract",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RentalContractLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PlannedFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PlannedTo = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActualOut = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActualIn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MeterOut = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    MeterIn = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Basis = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RateOverridden = table.Column<bool>(type: "bit", nullable: false),
                    WithOperator = table.Column<bool>(type: "bit", nullable: false),
                    OperatorRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OperatorEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IncludedHours = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OvertimeRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    MinimumPeriods = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    EstimatedAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BilledThrough = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RentalContractLine", x => x.Id);
                    table.ForeignKey(name: "FK_RentalContractLine_Employee_OperatorEmployeeId", column: x => x.OperatorEmployeeId, principalTable: "Employee",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_RentalContractLine_Equipment_EquipmentId", column: x => x.EquipmentId, principalTable: "Equipment",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_RentalContractLine_RentalContract_ContractId", column: x => x.ContractId, principalTable: "RentalContract",
                        principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(name: "IX_RentalContract_BookingId", table: "RentalContract", column: "BookingId");
            migrationBuilder.CreateIndex(name: "IX_RentalContract_CustomerId", table: "RentalContract", column: "CustomerId");
            migrationBuilder.CreateIndex(name: "IX_RentalContract_ParentContractId", table: "RentalContract", column: "ParentContractId");
            migrationBuilder.CreateIndex(name: "IX_RentalContract_TenantId", table: "RentalContract", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_RentalContract_WarehouseId", table: "RentalContract", column: "WarehouseId");
            migrationBuilder.CreateIndex(name: "IX_RentalContract_TenantId_ContractNumber", table: "RentalContract", columns: new[] { "TenantId", "ContractNumber" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_RentalContract_TenantId_CustomerId", table: "RentalContract", columns: new[] { "TenantId", "CustomerId" });
            migrationBuilder.CreateIndex(name: "IX_RentalContract_TenantId_Status", table: "RentalContract", columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(name: "IX_RentalContractChange_ContractId", table: "RentalContractChange", column: "ContractId");
            migrationBuilder.CreateIndex(name: "IX_RentalContractChange_NewContractId", table: "RentalContractChange", column: "NewContractId");
            migrationBuilder.CreateIndex(name: "IX_RentalContractChange_TenantId", table: "RentalContractChange", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_RentalContractChange_TenantId_ContractId", table: "RentalContractChange", columns: new[] { "TenantId", "ContractId" });

            migrationBuilder.CreateIndex(name: "IX_RentalContractLine_ContractId", table: "RentalContractLine", column: "ContractId");
            migrationBuilder.CreateIndex(name: "IX_RentalContractLine_EquipmentId", table: "RentalContractLine", column: "EquipmentId");
            migrationBuilder.CreateIndex(name: "IX_RentalContractLine_OperatorEmployeeId", table: "RentalContractLine", column: "OperatorEmployeeId");
            migrationBuilder.CreateIndex(name: "IX_RentalContractLine_TenantId", table: "RentalContractLine", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_RentalContractLine_TenantId_EquipmentId_PlannedFrom_PlannedTo", table: "RentalContractLine",
                columns: new[] { "TenantId", "EquipmentId", "PlannedFrom", "PlannedTo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RentalContractChange");
            migrationBuilder.DropTable(name: "RentalContractLine");
            migrationBuilder.DropTable(name: "RentalContract");
            migrationBuilder.DropColumn(name: "ContractId", table: "RentalBooking");
        }
    }
}
