using System;
using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// ورش السيارات W4: أعمدة الفوترة والتسليم والضمان على أمر العمل (جدول الوحدة نفسها)، وجدول ضمانات الأسطر.
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261121150000_WorkshopBillingWarranty")]
    public partial class WorkshopBillingWarranty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(name: "CostAmount", table: "WorkOrder", type: "decimal(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<Guid>(name: "CostJournalEntryId", table: "WorkOrder", type: "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<string>(name: "DeliveredBy", table: "WorkOrder", type: "nvarchar(200)", maxLength: 200, nullable: true);
            migrationBuilder.AddColumn<string>(name: "DeliveredTo", table: "WorkOrder", type: "nvarchar(200)", maxLength: 200, nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "InvoiceId", table: "WorkOrder", type: "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<string>(name: "InvoiceNumber", table: "WorkOrder", type: "nvarchar(50)", maxLength: 50, nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "InvoicedAt", table: "WorkOrder", type: "datetime2", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "IsWarrantyJob", table: "WorkOrder", type: "bit", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<Guid>(name: "WarrantyId", table: "WorkOrder", type: "uniqueidentifier", nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkshopWarranty",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    KmLimit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ClaimWorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkshopWarranty", x => x.Id);
                    table.ForeignKey(name: "FK_WorkshopWarranty_ServiceVehicle_VehicleId", column: x => x.VehicleId, principalTable: "ServiceVehicle",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkshopWarranty_WorkOrderLine_LineId", column: x => x.LineId, principalTable: "WorkOrderLine",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkshopWarranty_WorkOrder_ClaimWorkOrderId", column: x => x.ClaimWorkOrderId, principalTable: "WorkOrder",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkshopWarranty_WorkOrder_WorkOrderId", column: x => x.WorkOrderId, principalTable: "WorkOrder",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(name: "IX_WorkOrder_WarrantyId", table: "WorkOrder", column: "WarrantyId");
            migrationBuilder.CreateIndex(name: "IX_WorkshopWarranty_ClaimWorkOrderId", table: "WorkshopWarranty", column: "ClaimWorkOrderId");
            migrationBuilder.CreateIndex(name: "IX_WorkshopWarranty_LineId", table: "WorkshopWarranty", column: "LineId");
            migrationBuilder.CreateIndex(name: "IX_WorkshopWarranty_TenantId", table: "WorkshopWarranty", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_WorkshopWarranty_VehicleId", table: "WorkshopWarranty", column: "VehicleId");
            migrationBuilder.CreateIndex(name: "IX_WorkshopWarranty_WorkOrderId", table: "WorkshopWarranty", column: "WorkOrderId");
            migrationBuilder.CreateIndex(name: "IX_WorkshopWarranty_TenantId_VehicleId", table: "WorkshopWarranty", columns: new[] { "TenantId", "VehicleId" });

            migrationBuilder.AddForeignKey(name: "FK_WorkOrder_WorkshopWarranty_WarrantyId", table: "WorkOrder", column: "WarrantyId",
                principalTable: "WorkshopWarranty", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_WorkOrder_WorkshopWarranty_WarrantyId", table: "WorkOrder");
            migrationBuilder.DropTable(name: "WorkshopWarranty");
            migrationBuilder.DropIndex(name: "IX_WorkOrder_WarrantyId", table: "WorkOrder");
            migrationBuilder.DropColumn(name: "CostAmount", table: "WorkOrder");
            migrationBuilder.DropColumn(name: "CostJournalEntryId", table: "WorkOrder");
            migrationBuilder.DropColumn(name: "DeliveredBy", table: "WorkOrder");
            migrationBuilder.DropColumn(name: "DeliveredTo", table: "WorkOrder");
            migrationBuilder.DropColumn(name: "InvoiceId", table: "WorkOrder");
            migrationBuilder.DropColumn(name: "InvoiceNumber", table: "WorkOrder");
            migrationBuilder.DropColumn(name: "InvoicedAt", table: "WorkOrder");
            migrationBuilder.DropColumn(name: "IsWarrantyJob", table: "WorkOrder");
            migrationBuilder.DropColumn(name: "WarrantyId", table: "WorkOrder");
        }
    }
}
