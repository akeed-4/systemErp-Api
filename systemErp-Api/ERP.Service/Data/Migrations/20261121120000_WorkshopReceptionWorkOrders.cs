using System;
using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// ورش السيارات W1: الإعداد والمراحل وأسباب التوقف وتعريفات الخدمات وقوالب الفحص، وسجل مركبات العملاء، والاستقبال وأوامر العمل وسجل
    /// حالاتها وأضرار الاستقبال. جداول جديدة فقط؛ لا تعديل على جداول قائمة.
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261121120000_WorkshopReceptionWorkOrders")]
    public partial class WorkshopReceptionWorkOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkshopSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefaultLaborRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ReservationHoldDays = table.Column<int>(type: "int", nullable: false),
                    DefaultWarrantyMonths = table.Column<int>(type: "int", nullable: false),
                    DefaultWarrantyKm = table.Column<int>(type: "int", nullable: false),
                    DefaultWarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequireReceptionInspection = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkshopSettings", x => x.Id);
                    table.ForeignKey(name: "FK_WorkshopSettings_Warehouse_DefaultWarehouseId", column: x => x.DefaultWarehouseId, principalTable: "Warehouse",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkshopStage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_WorkshopStage", x => x.Id));

            migrationBuilder.CreateTable(
                name: "WorkshopPauseReason",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_WorkshopPauseReason", x => x.Id));

            migrationBuilder.CreateTable(
                name: "WorkshopServiceDefinition",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    StandardHours = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PricingMethod = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    WarrantyMonths = table.Column<int>(type: "int", nullable: false),
                    WarrantyKm = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkshopServiceDefinition", x => x.Id);
                    table.ForeignKey(name: "FK_WorkshopServiceDefinition_Product_ProductId", column: x => x.ProductId, principalTable: "Product",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InspectionTemplate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Usage = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_InspectionTemplate", x => x.Id));

            migrationBuilder.CreateTable(
                name: "InspectionTemplateItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Section = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TextAr = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    TextEn = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ResultType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PhotoRequiredOnFail = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionTemplateItem", x => x.Id);
                    table.ForeignKey(name: "FK_InspectionTemplateItem_InspectionTemplate_TemplateId", column: x => x.TemplateId, principalTable: "InspectionTemplate",
                        principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServiceVehicle",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Vin = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    PlateNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Make = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Year = table.Column<int>(type: "int", nullable: true),
                    Color = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    EngineNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FuelType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    LastOdometer = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ShowroomVehicleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceVehicle", x => x.Id);
                    table.ForeignKey(name: "FK_ServiceVehicle_Customer_CustomerId", column: x => x.CustomerId, principalTable: "Customer",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_ServiceVehicle_Vehicle_ShowroomVehicleId", column: x => x.ShowroomVehicleId, principalTable: "Vehicle",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkshopReception",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceptionNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ContactName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ContactPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkshopReception", x => x.Id);
                    table.ForeignKey(name: "FK_WorkshopReception_Customer_CustomerId", column: x => x.CustomerId, principalTable: "Customer",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkshopReception_Warehouse_WarehouseId", column: x => x.WarehouseId, principalTable: "Warehouse",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReceptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CustomerPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    VehicleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlateSnapshot = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    StageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PauseReasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Priority = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OdometerIn = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OdometerOut = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    FuelLevelIn = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Complaint = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Accessories = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ServiceAdvisorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ServiceAdvisorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PromisedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReadyAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrder", x => x.Id);
                    table.ForeignKey(name: "FK_WorkOrder_Customer_CustomerId", column: x => x.CustomerId, principalTable: "Customer",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkOrder_ServiceVehicle_VehicleId", column: x => x.VehicleId, principalTable: "ServiceVehicle",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkOrder_Warehouse_WarehouseId", column: x => x.WarehouseId, principalTable: "Warehouse",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkOrder_WorkshopPauseReason_PauseReasonId", column: x => x.PauseReasonId, principalTable: "WorkshopPauseReason",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkOrder_WorkshopReception_ReceptionId", column: x => x.ReceptionId, principalTable: "WorkshopReception",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkOrder_WorkshopStage_StageId", column: x => x.StageId, principalTable: "WorkshopStage",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderDamageMark",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Area = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DamageType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderDamageMark", x => x.Id);
                    table.ForeignKey(name: "FK_WorkOrderDamageMark_WorkOrder_WorkOrderId", column: x => x.WorkOrderId, principalTable: "WorkOrder",
                        principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderStatusHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    StageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PauseReasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ChangedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ChangedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderStatusHistory", x => x.Id);
                    table.ForeignKey(name: "FK_WorkOrderStatusHistory_WorkOrder_WorkOrderId", column: x => x.WorkOrderId, principalTable: "WorkOrder",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkOrderStatusHistory_WorkshopPauseReason_PauseReasonId", column: x => x.PauseReasonId, principalTable: "WorkshopPauseReason",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkOrderStatusHistory_WorkshopStage_StageId", column: x => x.StageId, principalTable: "WorkshopStage",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(name: "IX_WorkshopSettings_DefaultWarehouseId", table: "WorkshopSettings", column: "DefaultWarehouseId");
            migrationBuilder.CreateIndex(name: "IX_WorkshopSettings_TenantId", table: "WorkshopSettings", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_WorkshopStage_TenantId", table: "WorkshopStage", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_WorkshopPauseReason_TenantId", table: "WorkshopPauseReason", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_WorkshopServiceDefinition_ProductId", table: "WorkshopServiceDefinition", column: "ProductId");
            migrationBuilder.CreateIndex(name: "IX_WorkshopServiceDefinition_TenantId", table: "WorkshopServiceDefinition", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_WorkshopServiceDefinition_TenantId_Code", table: "WorkshopServiceDefinition", columns: new[] { "TenantId", "Code" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_InspectionTemplate_TenantId", table: "InspectionTemplate", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_InspectionTemplateItem_TemplateId", table: "InspectionTemplateItem", column: "TemplateId");
            migrationBuilder.CreateIndex(name: "IX_InspectionTemplateItem_TenantId", table: "InspectionTemplateItem", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_ServiceVehicle_CustomerId", table: "ServiceVehicle", column: "CustomerId");
            migrationBuilder.CreateIndex(name: "IX_ServiceVehicle_ShowroomVehicleId", table: "ServiceVehicle", column: "ShowroomVehicleId");
            migrationBuilder.CreateIndex(name: "IX_ServiceVehicle_TenantId", table: "ServiceVehicle", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_ServiceVehicle_TenantId_PlateNumber", table: "ServiceVehicle", columns: new[] { "TenantId", "PlateNumber" });
            migrationBuilder.CreateIndex(name: "IX_ServiceVehicle_TenantId_Vin", table: "ServiceVehicle", columns: new[] { "TenantId", "Vin" }, unique: true,
                filter: "[Vin] IS NOT NULL");
            migrationBuilder.CreateIndex(name: "IX_WorkshopReception_CustomerId", table: "WorkshopReception", column: "CustomerId");
            migrationBuilder.CreateIndex(name: "IX_WorkshopReception_TenantId", table: "WorkshopReception", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_WorkshopReception_WarehouseId", table: "WorkshopReception", column: "WarehouseId");
            migrationBuilder.CreateIndex(name: "IX_WorkshopReception_TenantId_ReceptionNumber", table: "WorkshopReception", columns: new[] { "TenantId", "ReceptionNumber" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_WorkOrder_CustomerId", table: "WorkOrder", column: "CustomerId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrder_PauseReasonId", table: "WorkOrder", column: "PauseReasonId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrder_ReceptionId", table: "WorkOrder", column: "ReceptionId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrder_StageId", table: "WorkOrder", column: "StageId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrder_TenantId", table: "WorkOrder", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrder_VehicleId", table: "WorkOrder", column: "VehicleId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrder_WarehouseId", table: "WorkOrder", column: "WarehouseId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrder_TenantId_OrderNumber", table: "WorkOrder", columns: new[] { "TenantId", "OrderNumber" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_WorkOrder_TenantId_Status", table: "WorkOrder", columns: new[] { "TenantId", "Status" });
            migrationBuilder.CreateIndex(name: "IX_WorkOrder_TenantId_VehicleId", table: "WorkOrder", columns: new[] { "TenantId", "VehicleId" });
            migrationBuilder.CreateIndex(name: "IX_WorkOrderDamageMark_TenantId", table: "WorkOrderDamageMark", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderDamageMark_WorkOrderId", table: "WorkOrderDamageMark", column: "WorkOrderId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderStatusHistory_PauseReasonId", table: "WorkOrderStatusHistory", column: "PauseReasonId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderStatusHistory_StageId", table: "WorkOrderStatusHistory", column: "StageId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderStatusHistory_TenantId", table: "WorkOrderStatusHistory", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderStatusHistory_WorkOrderId", table: "WorkOrderStatusHistory", column: "WorkOrderId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderStatusHistory_TenantId_WorkOrderId", table: "WorkOrderStatusHistory", columns: new[] { "TenantId", "WorkOrderId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WorkOrderStatusHistory");
            migrationBuilder.DropTable(name: "WorkOrderDamageMark");
            migrationBuilder.DropTable(name: "WorkOrder");
            migrationBuilder.DropTable(name: "WorkshopReception");
            migrationBuilder.DropTable(name: "ServiceVehicle");
            migrationBuilder.DropTable(name: "InspectionTemplateItem");
            migrationBuilder.DropTable(name: "InspectionTemplate");
            migrationBuilder.DropTable(name: "WorkshopServiceDefinition");
            migrationBuilder.DropTable(name: "WorkshopPauseReason");
            migrationBuilder.DropTable(name: "WorkshopStage");
            migrationBuilder.DropTable(name: "WorkshopSettings");
        }
    }
}
