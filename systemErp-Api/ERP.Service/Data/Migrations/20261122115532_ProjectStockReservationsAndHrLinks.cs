using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProjectStockReservationsAndHrLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProjectMember_TenantId_ContractId_UserId",
                table: "ProjectMember");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "ProjectMember",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "EmployeeId",
                table: "ProjectMember",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "MaterialRequisition",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestedByEmployeeId",
                table: "MaterialRequisition",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StockReservation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IssuedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ReleasedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SourceTransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockReservation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockReservation_CommercialContract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "CommercialContract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockReservation_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockReservation_Warehouse_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouse",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockReservationConsumption",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssueLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IsReversed = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockReservationConsumption", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockReservationConsumption_ProjectMaterialIssue_IssueId",
                        column: x => x.IssueId,
                        principalTable: "ProjectMaterialIssue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockReservationConsumption_StockReservation_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "StockReservation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockReservationTransfer",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TargetReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockReservationTransfer", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockReservationTransfer_StockReservation_SourceReservationId",
                        column: x => x.SourceReservationId,
                        principalTable: "StockReservation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMember_EmployeeId",
                table: "ProjectMember",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMember_TenantId_ContractId_EmployeeId",
                table: "ProjectMember",
                columns: new[] { "TenantId", "ContractId", "EmployeeId" },
                unique: true,
                filter: "[EmployeeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMember_TenantId_ContractId_UserId",
                table: "ProjectMember",
                columns: new[] { "TenantId", "ContractId", "UserId" },
                unique: true,
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRequisition_DepartmentId",
                table: "MaterialRequisition",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRequisition_RequestedByEmployeeId",
                table: "MaterialRequisition",
                column: "RequestedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservation_ContractId",
                table: "StockReservation",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservation_ProductId",
                table: "StockReservation",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservation_TenantId",
                table: "StockReservation",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservation_TenantId_ContractId",
                table: "StockReservation",
                columns: new[] { "TenantId", "ContractId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockReservation_TenantId_ProductId_WarehouseId_Status",
                table: "StockReservation",
                columns: new[] { "TenantId", "ProductId", "WarehouseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StockReservation_WarehouseId",
                table: "StockReservation",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservationConsumption_IssueId",
                table: "StockReservationConsumption",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservationConsumption_ReservationId",
                table: "StockReservationConsumption",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservationConsumption_TenantId",
                table: "StockReservationConsumption",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservationConsumption_TenantId_IssueId",
                table: "StockReservationConsumption",
                columns: new[] { "TenantId", "IssueId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockReservationConsumption_TenantId_ReservationId",
                table: "StockReservationConsumption",
                columns: new[] { "TenantId", "ReservationId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockReservationTransfer_SourceReservationId",
                table: "StockReservationTransfer",
                column: "SourceReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservationTransfer_TenantId",
                table: "StockReservationTransfer",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservationTransfer_TenantId_SourceContractId",
                table: "StockReservationTransfer",
                columns: new[] { "TenantId", "SourceContractId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockReservationTransfer_TenantId_Status",
                table: "StockReservationTransfer",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StockReservationTransfer_TenantId_TargetContractId",
                table: "StockReservationTransfer",
                columns: new[] { "TenantId", "TargetContractId" });

            migrationBuilder.AddForeignKey(
                name: "FK_MaterialRequisition_Department_DepartmentId",
                table: "MaterialRequisition",
                column: "DepartmentId",
                principalTable: "Department",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MaterialRequisition_Employee_RequestedByEmployeeId",
                table: "MaterialRequisition",
                column: "RequestedByEmployeeId",
                principalTable: "Employee",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectMember_Employee_EmployeeId",
                table: "ProjectMember",
                column: "EmployeeId",
                principalTable: "Employee",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // العضوية القديمة بالمستخدم: تُربط بملف الموظف الذي يحمل الحساب نفسه في المنشأة نفسها (بالمعرّف، لا بالاسم)، وحين يكون
            // للحساب موظف واحد فقط. ما لا يُطابَق يبقى كما هو بمستخدمه. طلبات الشراء القديمة تبقى على نص القسم والمقدّم دون تخمين.
            migrationBuilder.Sql("""
                UPDATE m SET m.EmployeeId = e.Id
                FROM ProjectMember m
                JOIN Employee e ON e.TenantId = m.TenantId AND e.UserId = m.UserId
                WHERE m.EmployeeId IS NULL AND m.UserId IS NOT NULL
                  AND (SELECT COUNT(*) FROM Employee x WHERE x.TenantId = m.TenantId AND x.UserId = m.UserId) = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MaterialRequisition_Department_DepartmentId",
                table: "MaterialRequisition");

            migrationBuilder.DropForeignKey(
                name: "FK_MaterialRequisition_Employee_RequestedByEmployeeId",
                table: "MaterialRequisition");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectMember_Employee_EmployeeId",
                table: "ProjectMember");

            migrationBuilder.DropTable(
                name: "StockReservationConsumption");

            migrationBuilder.DropTable(
                name: "StockReservationTransfer");

            migrationBuilder.DropTable(
                name: "StockReservation");

            migrationBuilder.DropIndex(
                name: "IX_ProjectMember_EmployeeId",
                table: "ProjectMember");

            migrationBuilder.DropIndex(
                name: "IX_ProjectMember_TenantId_ContractId_EmployeeId",
                table: "ProjectMember");

            migrationBuilder.DropIndex(
                name: "IX_ProjectMember_TenantId_ContractId_UserId",
                table: "ProjectMember");

            migrationBuilder.DropIndex(
                name: "IX_MaterialRequisition_DepartmentId",
                table: "MaterialRequisition");

            migrationBuilder.DropIndex(
                name: "IX_MaterialRequisition_RequestedByEmployeeId",
                table: "MaterialRequisition");

            migrationBuilder.DropColumn(
                name: "EmployeeId",
                table: "ProjectMember");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "MaterialRequisition");

            migrationBuilder.DropColumn(
                name: "RequestedByEmployeeId",
                table: "MaterialRequisition");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "ProjectMember",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMember_TenantId_ContractId_UserId",
                table: "ProjectMember",
                columns: new[] { "TenantId", "ContractId", "UserId" },
                unique: true);
        }
    }
}
