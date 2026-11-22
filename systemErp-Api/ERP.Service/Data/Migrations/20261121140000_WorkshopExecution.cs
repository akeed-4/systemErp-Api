using System;
using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// ورش السيارات W3: ساعات الفنيين، وحجز القطع وصرفها وإرجاعها، والخدمات الخارجية؛ وعمود اختياري <c>MaterialRequisition.WorkOrderId</c>
    /// (D7) بلا قيمة للطلبات القائمة فلا يتغير سلوكها.
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261121140000_WorkshopExecution")]
    public partial class WorkshopExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(name: "WorkOrderId", table: "MaterialRequisition", type: "uniqueidentifier", nullable: true);
            migrationBuilder.CreateIndex(name: "IX_MaterialRequisition_WorkOrderId", table: "MaterialRequisition", column: "WorkOrderId");
            migrationBuilder.AddForeignKey(name: "FK_MaterialRequisition_WorkOrder_WorkOrderId", table: "MaterialRequisition", column: "WorkOrderId",
                principalTable: "WorkOrder", principalColumn: "Id", onDelete: ReferentialAction.Restrict);

            migrationBuilder.CreateTable(
                name: "WorkOrderTimeLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PauseReasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Minutes = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CostPerHour = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderTimeLog", x => x.Id);
                    table.ForeignKey(name: "FK_WorkOrderTimeLog_Employee_EmployeeId", column: x => x.EmployeeId, principalTable: "Employee",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkOrderTimeLog_WorkOrderLine_LineId", column: x => x.LineId, principalTable: "WorkOrderLine",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkOrderTimeLog_WorkOrder_WorkOrderId", column: x => x.WorkOrderId, principalTable: "WorkOrder",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkOrderTimeLog_WorkshopPauseReason_PauseReasonId", column: x => x.PauseReasonId, principalTable: "WorkshopPauseReason",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderPartReservation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ConsumedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderPartReservation", x => x.Id);
                    table.ForeignKey(name: "FK_WorkOrderPartReservation_Product_ProductId", column: x => x.ProductId, principalTable: "Product",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkOrderPartReservation_Warehouse_WarehouseId", column: x => x.WarehouseId, principalTable: "Warehouse",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkOrderPartReservation_WorkOrderLine_LineId", column: x => x.LineId, principalTable: "WorkOrderLine",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkOrderPartReservation_WorkOrder_WorkOrderId", column: x => x.WorkOrderId, principalTable: "WorkOrder",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderPartIssue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ReturnOfId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StockMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderPartIssue", x => x.Id);
                    table.ForeignKey(name: "FK_WorkOrderPartIssue_Product_ProductId", column: x => x.ProductId, principalTable: "Product",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkOrderPartIssue_Warehouse_WarehouseId", column: x => x.WarehouseId, principalTable: "Warehouse",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkOrderPartIssue_WorkOrderLine_LineId", column: x => x.LineId, principalTable: "WorkOrderLine",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkOrderPartIssue_WorkOrderPartIssue_ReturnOfId", column: x => x.ReturnOfId, principalTable: "WorkOrderPartIssue",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkOrderPartIssue_WorkOrder_WorkOrderId", column: x => x.WorkOrderId, principalTable: "WorkOrder",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderExternalService",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    EstimatedCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ActualCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PurchaseInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderExternalService", x => x.Id);
                    table.ForeignKey(name: "FK_WorkOrderExternalService_Supplier_SupplierId", column: x => x.SupplierId, principalTable: "Supplier",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkOrderExternalService_WorkOrderLine_LineId", column: x => x.LineId, principalTable: "WorkOrderLine",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkOrderExternalService_WorkOrder_WorkOrderId", column: x => x.WorkOrderId, principalTable: "WorkOrder",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(name: "IX_WorkOrderTimeLog_EmployeeId", table: "WorkOrderTimeLog", column: "EmployeeId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderTimeLog_LineId", table: "WorkOrderTimeLog", column: "LineId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderTimeLog_PauseReasonId", table: "WorkOrderTimeLog", column: "PauseReasonId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderTimeLog_TenantId", table: "WorkOrderTimeLog", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderTimeLog_WorkOrderId", table: "WorkOrderTimeLog", column: "WorkOrderId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderTimeLog_TenantId_EmployeeId_Open", table: "WorkOrderTimeLog", columns: new[] { "TenantId", "EmployeeId" },
                unique: true, filter: "[EndedAt] IS NULL");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderTimeLog_TenantId_WorkOrderId", table: "WorkOrderTimeLog", columns: new[] { "TenantId", "WorkOrderId" });

            migrationBuilder.CreateIndex(name: "IX_WorkOrderPartReservation_LineId", table: "WorkOrderPartReservation", column: "LineId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderPartReservation_ProductId", table: "WorkOrderPartReservation", column: "ProductId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderPartReservation_TenantId", table: "WorkOrderPartReservation", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderPartReservation_WarehouseId", table: "WorkOrderPartReservation", column: "WarehouseId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderPartReservation_WorkOrderId", table: "WorkOrderPartReservation", column: "WorkOrderId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderPartReservation_TenantId_ProductId_WarehouseId_Status", table: "WorkOrderPartReservation",
                columns: new[] { "TenantId", "ProductId", "WarehouseId", "Status" });

            migrationBuilder.CreateIndex(name: "IX_WorkOrderPartIssue_LineId", table: "WorkOrderPartIssue", column: "LineId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderPartIssue_ProductId", table: "WorkOrderPartIssue", column: "ProductId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderPartIssue_ReturnOfId", table: "WorkOrderPartIssue", column: "ReturnOfId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderPartIssue_TenantId", table: "WorkOrderPartIssue", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderPartIssue_WarehouseId", table: "WorkOrderPartIssue", column: "WarehouseId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderPartIssue_WorkOrderId", table: "WorkOrderPartIssue", column: "WorkOrderId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderPartIssue_TenantId_WorkOrderId", table: "WorkOrderPartIssue", columns: new[] { "TenantId", "WorkOrderId" });

            migrationBuilder.CreateIndex(name: "IX_WorkOrderExternalService_LineId", table: "WorkOrderExternalService", column: "LineId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderExternalService_SupplierId", table: "WorkOrderExternalService", column: "SupplierId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderExternalService_TenantId", table: "WorkOrderExternalService", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderExternalService_WorkOrderId", table: "WorkOrderExternalService", column: "WorkOrderId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderExternalService_TenantId_WorkOrderId", table: "WorkOrderExternalService", columns: new[] { "TenantId", "WorkOrderId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WorkOrderExternalService");
            migrationBuilder.DropTable(name: "WorkOrderPartIssue");
            migrationBuilder.DropTable(name: "WorkOrderPartReservation");
            migrationBuilder.DropTable(name: "WorkOrderTimeLog");
            migrationBuilder.DropForeignKey(name: "FK_MaterialRequisition_WorkOrder_WorkOrderId", table: "MaterialRequisition");
            migrationBuilder.DropIndex(name: "IX_MaterialRequisition_WorkOrderId", table: "MaterialRequisition");
            migrationBuilder.DropColumn(name: "WorkOrderId", table: "MaterialRequisition");
        }
    }
}
