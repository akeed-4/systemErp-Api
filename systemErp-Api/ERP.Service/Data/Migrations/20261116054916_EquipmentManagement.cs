using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class EquipmentManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Equipment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Brand = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ManufactureYear = table.Column<int>(type: "int", nullable: true),
                    SerialNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PlateNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Capacity = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Specifications = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Ownership = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    FixedAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LibraryResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RateBasis = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    FuelPerHour = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    MaintenancePerHour = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OperatorPerHour = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    HoursPerDay = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DaysPerMonth = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    MeterHours = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ServiceIntervalHours = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    ServiceIntervalDays = table.Column<int>(type: "int", nullable: true),
                    LastServiceMeter = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    LastServiceDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Equipment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Equipment_CostCenter_CostCenterId",
                        column: x => x.CostCenterId,
                        principalTable: "CostCenter",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Equipment_FixedAsset_FixedAssetId",
                        column: x => x.FixedAssetId,
                        principalTable: "FixedAsset",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Equipment_Supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EquipmentAssignment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SiteName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OperatorEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FromDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ToDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    MeterStart = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    MeterEnd = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    TransferredFromId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReturnNotes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentAssignment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentAssignment_CommercialContract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "CommercialContract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentAssignment_Employee_OperatorEmployeeId",
                        column: x => x.OperatorEmployeeId,
                        principalTable: "Employee",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentAssignment_Equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentAssignment_ProjectResource_ProjectResourceId",
                        column: x => x.ProjectResourceId,
                        principalTable: "ProjectResource",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EquipmentLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Hours = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    MeterReading = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ChargeToProject = table.Column<bool>(type: "bit", nullable: false),
                    ExpenseAccountCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CreditAccountCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentLog_Equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EquipmentMaintenanceOrder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ScheduledDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MeterReading = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    WorkDone = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    TechnicianEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LaborCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ExternalCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PartsCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DowntimeHours = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChargeToProject = table.Column<bool>(type: "bit", nullable: false),
                    ExpenseAccountCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CreditAccountCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StatusBefore = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CompletedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentMaintenanceOrder", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentMaintenanceOrder_Equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentMaintenanceOrder_Warehouse_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouse",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EquipmentRentalContract",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RateBasis = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IdleBillingPercent = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    StandbyBillingPercent = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    HoursPerDay = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DaysPerMonth = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DepositAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Terms = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentRentalContract", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentRentalContract_Equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentRentalContract_Supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EquipmentMaintenancePart",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentMaintenancePart", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentMaintenancePart_EquipmentMaintenanceOrder_OrderId",
                        column: x => x.OrderId,
                        principalTable: "EquipmentMaintenanceOrder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EquipmentMaintenancePart_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Equipment_CostCenterId",
                table: "Equipment",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_Equipment_FixedAssetId",
                table: "Equipment",
                column: "FixedAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_Equipment_SupplierId",
                table: "Equipment",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_Equipment_TenantId",
                table: "Equipment",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Equipment_TenantId_Code",
                table: "Equipment",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Equipment_TenantId_FixedAssetId",
                table: "Equipment",
                columns: new[] { "TenantId", "FixedAssetId" },
                unique: true,
                filter: "[FixedAssetId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentAssignment_ContractId",
                table: "EquipmentAssignment",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentAssignment_EquipmentId",
                table: "EquipmentAssignment",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentAssignment_OperatorEmployeeId",
                table: "EquipmentAssignment",
                column: "OperatorEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentAssignment_ProjectResourceId",
                table: "EquipmentAssignment",
                column: "ProjectResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentAssignment_TenantId",
                table: "EquipmentAssignment",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentAssignment_TenantId_ContractId",
                table: "EquipmentAssignment",
                columns: new[] { "TenantId", "ContractId" });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentAssignment_TenantId_EquipmentId",
                table: "EquipmentAssignment",
                columns: new[] { "TenantId", "EquipmentId" },
                unique: true,
                filter: "[Status] = N'active'");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentLog_EquipmentId",
                table: "EquipmentLog",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentLog_TenantId",
                table: "EquipmentLog",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentLog_TenantId_EquipmentId_Date",
                table: "EquipmentLog",
                columns: new[] { "TenantId", "EquipmentId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentMaintenanceOrder_EquipmentId",
                table: "EquipmentMaintenanceOrder",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentMaintenanceOrder_TenantId",
                table: "EquipmentMaintenanceOrder",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentMaintenanceOrder_TenantId_EquipmentId_Status",
                table: "EquipmentMaintenanceOrder",
                columns: new[] { "TenantId", "EquipmentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentMaintenanceOrder_TenantId_OrderNumber",
                table: "EquipmentMaintenanceOrder",
                columns: new[] { "TenantId", "OrderNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentMaintenanceOrder_WarehouseId",
                table: "EquipmentMaintenanceOrder",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentMaintenancePart_OrderId",
                table: "EquipmentMaintenancePart",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentMaintenancePart_ProductId",
                table: "EquipmentMaintenancePart",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentMaintenancePart_TenantId",
                table: "EquipmentMaintenancePart",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentRentalContract_EquipmentId",
                table: "EquipmentRentalContract",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentRentalContract_SupplierId",
                table: "EquipmentRentalContract",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentRentalContract_TenantId",
                table: "EquipmentRentalContract",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentRentalContract_TenantId_ContractNumber",
                table: "EquipmentRentalContract",
                columns: new[] { "TenantId", "ContractNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentRentalContract_TenantId_EquipmentId_Status",
                table: "EquipmentRentalContract",
                columns: new[] { "TenantId", "EquipmentId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EquipmentAssignment");

            migrationBuilder.DropTable(
                name: "EquipmentLog");

            migrationBuilder.DropTable(
                name: "EquipmentMaintenancePart");

            migrationBuilder.DropTable(
                name: "EquipmentRentalContract");

            migrationBuilder.DropTable(
                name: "EquipmentMaintenanceOrder");

            migrationBuilder.DropTable(
                name: "Equipment");
        }
    }
}
