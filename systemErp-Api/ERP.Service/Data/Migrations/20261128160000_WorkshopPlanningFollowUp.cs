using System;
using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// ورش السيارات (التخطيط والمتابعة): المنصات، والمواعيد، والمتابعات، وأعمدة التخطيط على أمر العمل، وإعداد المتابعة التلقائية.
    /// كل الأعمدة الجديدة على الجداول القائمة اختيارية فلا تمس البيانات الحالية.
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261128160000_WorkshopPlanningFollowUp")]
    public partial class WorkshopPlanningFollowUp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(name: "FollowUpCallDays", table: "WorkshopSettings", type: "int", nullable: true);
            migrationBuilder.AddColumn<int>(name: "ServiceReminderMonths", table: "WorkshopSettings", type: "int", nullable: true);
            migrationBuilder.AddColumn<int>(name: "ServiceReminderKm", table: "WorkshopSettings", type: "int", nullable: true);

            migrationBuilder.AddColumn<Guid>(name: "BayId", table: "WorkOrder", type: "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "AssignedTechnicianId", table: "WorkOrder", type: "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "PlannedStartAt", table: "WorkOrder", type: "datetime2", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "PlannedEndAt", table: "WorkOrder", type: "datetime2", nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkshopBay",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkshopBay", x => x.Id);
                    table.ForeignKey(name: "FK_WorkshopBay_Warehouse_WarehouseId", column: x => x.WarehouseId, principalTable: "Warehouse",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkshopAppointment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CustomerPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    VehicleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VehicleDescription = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BayId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ScheduledAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    Complaint = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkshopAppointment", x => x.Id);
                    table.ForeignKey(name: "FK_WorkshopAppointment_Customer_CustomerId", column: x => x.CustomerId, principalTable: "Customer",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkshopAppointment_ServiceVehicle_VehicleId", column: x => x.VehicleId, principalTable: "ServiceVehicle",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkshopAppointment_Warehouse_WarehouseId", column: x => x.WarehouseId, principalTable: "Warehouse",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkshopAppointment_WorkshopBay_BayId", column: x => x.BayId, principalTable: "WorkshopBay",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkshopAppointment_WorkOrder_WorkOrderId", column: x => x.WorkOrderId, principalTable: "WorkOrder",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkshopFollowUp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DueOdometer = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkshopFollowUp", x => x.Id);
                    table.ForeignKey(name: "FK_WorkshopFollowUp_Customer_CustomerId", column: x => x.CustomerId, principalTable: "Customer",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkshopFollowUp_ServiceVehicle_VehicleId", column: x => x.VehicleId, principalTable: "ServiceVehicle",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkshopFollowUp_WorkOrder_WorkOrderId", column: x => x.WorkOrderId, principalTable: "WorkOrder",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkshopFollowUp_WorkshopAppointment_AppointmentId", column: x => x.AppointmentId, principalTable: "WorkshopAppointment",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(name: "IX_WorkOrder_BayId", table: "WorkOrder", column: "BayId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrder_AssignedTechnicianId", table: "WorkOrder", column: "AssignedTechnicianId");
            migrationBuilder.CreateIndex(name: "IX_WorkshopBay_TenantId", table: "WorkshopBay", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_WorkshopBay_TenantId_Code", table: "WorkshopBay", columns: new[] { "TenantId", "Code" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_WorkshopBay_WarehouseId", table: "WorkshopBay", column: "WarehouseId");
            migrationBuilder.CreateIndex(name: "IX_WorkshopAppointment_BayId", table: "WorkshopAppointment", column: "BayId");
            migrationBuilder.CreateIndex(name: "IX_WorkshopAppointment_CustomerId", table: "WorkshopAppointment", column: "CustomerId");
            migrationBuilder.CreateIndex(name: "IX_WorkshopAppointment_TenantId", table: "WorkshopAppointment", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_WorkshopAppointment_TenantId_ScheduledAt", table: "WorkshopAppointment", columns: new[] { "TenantId", "ScheduledAt" });
            migrationBuilder.CreateIndex(name: "IX_WorkshopAppointment_VehicleId", table: "WorkshopAppointment", column: "VehicleId");
            migrationBuilder.CreateIndex(name: "IX_WorkshopAppointment_WarehouseId", table: "WorkshopAppointment", column: "WarehouseId");
            migrationBuilder.CreateIndex(name: "IX_WorkshopAppointment_WorkOrderId", table: "WorkshopAppointment", column: "WorkOrderId");
            migrationBuilder.CreateIndex(name: "IX_WorkshopFollowUp_AppointmentId", table: "WorkshopFollowUp", column: "AppointmentId");
            migrationBuilder.CreateIndex(name: "IX_WorkshopFollowUp_CustomerId", table: "WorkshopFollowUp", column: "CustomerId");
            migrationBuilder.CreateIndex(name: "IX_WorkshopFollowUp_TenantId", table: "WorkshopFollowUp", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_WorkshopFollowUp_TenantId_Status_DueDate", table: "WorkshopFollowUp", columns: new[] { "TenantId", "Status", "DueDate" });
            migrationBuilder.CreateIndex(name: "IX_WorkshopFollowUp_VehicleId", table: "WorkshopFollowUp", column: "VehicleId");
            migrationBuilder.CreateIndex(name: "IX_WorkshopFollowUp_WorkOrderId", table: "WorkshopFollowUp", column: "WorkOrderId");

            migrationBuilder.AddForeignKey(name: "FK_WorkOrder_WorkshopBay_BayId", table: "WorkOrder", column: "BayId",
                principalTable: "WorkshopBay", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_WorkOrder_Employee_AssignedTechnicianId", table: "WorkOrder", column: "AssignedTechnicianId",
                principalTable: "Employee", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_WorkOrder_WorkshopBay_BayId", table: "WorkOrder");
            migrationBuilder.DropForeignKey(name: "FK_WorkOrder_Employee_AssignedTechnicianId", table: "WorkOrder");
            migrationBuilder.DropTable(name: "WorkshopFollowUp");
            migrationBuilder.DropTable(name: "WorkshopAppointment");
            migrationBuilder.DropTable(name: "WorkshopBay");
            migrationBuilder.DropIndex(name: "IX_WorkOrder_BayId", table: "WorkOrder");
            migrationBuilder.DropIndex(name: "IX_WorkOrder_AssignedTechnicianId", table: "WorkOrder");
            migrationBuilder.DropColumn(name: "BayId", table: "WorkOrder");
            migrationBuilder.DropColumn(name: "AssignedTechnicianId", table: "WorkOrder");
            migrationBuilder.DropColumn(name: "PlannedStartAt", table: "WorkOrder");
            migrationBuilder.DropColumn(name: "PlannedEndAt", table: "WorkOrder");
            migrationBuilder.DropColumn(name: "FollowUpCallDays", table: "WorkshopSettings");
            migrationBuilder.DropColumn(name: "ServiceReminderMonths", table: "WorkshopSettings");
            migrationBuilder.DropColumn(name: "ServiceReminderKm", table: "WorkshopSettings");
        }
    }
}
