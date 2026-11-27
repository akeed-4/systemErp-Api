using System;
using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>الخياطة — المرحلة 4: صرف المواد وإرجاعها، وأجور القطعة، وأجور الخياطين الخاصة. جداول جديدة فقط.</summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261128130000_TailoringCosting")]
    public partial class TailoringCosting : Migration
    {
        private static OperationBuilder<AddColumnOperation> Str(ColumnsBuilder t, int max, bool required = false)
            => t.Column<string>(type: $"nvarchar({max})", maxLength: max, nullable: !required);
        private static OperationBuilder<AddColumnOperation> Id(ColumnsBuilder t, bool nullable = false) => t.Column<Guid>(type: "uniqueidentifier", nullable: nullable);
        private static OperationBuilder<AddColumnOperation> Time(ColumnsBuilder t, bool nullable = false) => t.Column<DateTime>(type: "datetime2", nullable: nullable);
        private static OperationBuilder<AddColumnOperation> Dec(ColumnsBuilder t, int scale)
            => t.Column<decimal>(type: $"decimal(18,{scale})", precision: 18, scale: scale, nullable: false);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TailoringMaterialIssue",
                columns: t => new
                {
                    Id = Id(t), OrderId = Id(t), GarmentId = Id(t, true), ProductId = Id(t), WarehouseId = Id(t), Kind = Str(t, 16, true), ReturnOfId = Id(t, true),
                    Quantity = Dec(t, 4), UnitCost = Dec(t, 4), TotalCost = Dec(t, 2), Date = Time(t), Notes = Str(t, 500), StockMovementId = Id(t, true),
                    JournalEntryId = Id(t, true), CreatedBy = Str(t, 200), TenantId = Id(t), CreatedAt = Time(t), UpdatedAt = Time(t, true)
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_TailoringMaterialIssue", x => x.Id);
                    t.ForeignKey("FK_TailoringMaterialIssue_TailoringOrder_OrderId", x => x.OrderId, "TailoringOrder", "Id", onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_TailoringMaterialIssue_TailoringGarment_GarmentId", x => x.GarmentId, "TailoringGarment", "Id", onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_TailoringMaterialIssue_Product_ProductId", x => x.ProductId, "Product", "Id", onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_TailoringMaterialIssue_Warehouse_WarehouseId", x => x.WarehouseId, "Warehouse", "Id", onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_TailoringMaterialIssue_TailoringMaterialIssue_ReturnOfId", x => x.ReturnOfId, "TailoringMaterialIssue", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TailoringLaborEntry",
                columns: t => new
                {
                    Id = Id(t), OrderId = Id(t), GarmentId = Id(t), StageId = Id(t, true), EmployeeId = Id(t), Amount = Dec(t, 2), Date = Time(t),
                    IsManual = t.Column<bool>(type: "bit", nullable: false), Status = Str(t, 16, true), Notes = Str(t, 500), JournalEntryId = Id(t, true),
                    ReversalEntryId = Id(t, true), CancelReason = Str(t, 500), CreatedBy = Str(t, 200), TenantId = Id(t), CreatedAt = Time(t), UpdatedAt = Time(t, true)
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_TailoringLaborEntry", x => x.Id);
                    t.ForeignKey("FK_TailoringLaborEntry_TailoringOrder_OrderId", x => x.OrderId, "TailoringOrder", "Id", onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_TailoringLaborEntry_TailoringGarment_GarmentId", x => x.GarmentId, "TailoringGarment", "Id", onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_TailoringLaborEntry_TailoringStage_StageId", x => x.StageId, "TailoringStage", "Id", onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_TailoringLaborEntry_Employee_EmployeeId", x => x.EmployeeId, "Employee", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TailoringWorkerRate",
                columns: t => new { Id = Id(t), EmployeeId = Id(t), StageId = Id(t), Rate = Dec(t, 2), TenantId = Id(t), CreatedAt = Time(t), UpdatedAt = Time(t, true) },
                constraints: t =>
                {
                    t.PrimaryKey("PK_TailoringWorkerRate", x => x.Id);
                    t.ForeignKey("FK_TailoringWorkerRate_Employee_EmployeeId", x => x.EmployeeId, "Employee", "Id", onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_TailoringWorkerRate_TailoringStage_StageId", x => x.StageId, "TailoringStage", "Id", onDelete: ReferentialAction.Restrict);
                });

            foreach (var table in new[] { "TailoringMaterialIssue", "TailoringLaborEntry", "TailoringWorkerRate" })
                migrationBuilder.CreateIndex($"IX_{table}_TenantId", table, "TenantId");
            foreach (var (table, column) in new[]
            {
                ("TailoringMaterialIssue", "OrderId"), ("TailoringMaterialIssue", "GarmentId"), ("TailoringMaterialIssue", "ProductId"),
                ("TailoringMaterialIssue", "WarehouseId"), ("TailoringMaterialIssue", "ReturnOfId"),
                ("TailoringLaborEntry", "OrderId"), ("TailoringLaborEntry", "GarmentId"), ("TailoringLaborEntry", "StageId"), ("TailoringLaborEntry", "EmployeeId"),
                ("TailoringWorkerRate", "EmployeeId"), ("TailoringWorkerRate", "StageId"),
            })
                migrationBuilder.CreateIndex($"IX_{table}_{column}", table, column);
            migrationBuilder.CreateIndex("IX_TailoringMaterialIssue_TenantId_OrderId", "TailoringMaterialIssue", new[] { "TenantId", "OrderId" });
            migrationBuilder.CreateIndex("IX_TailoringLaborEntry_TenantId_EmployeeId_Date", "TailoringLaborEntry", new[] { "TenantId", "EmployeeId", "Date" });
            migrationBuilder.CreateIndex("IX_TailoringWorkerRate_TenantId_EmployeeId_StageId", "TailoringWorkerRate", new[] { "TenantId", "EmployeeId", "StageId" }, unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "TailoringWorkerRate", "TailoringLaborEntry", "TailoringMaterialIssue" })
                migrationBuilder.DropTable(table);
        }
    }
}
