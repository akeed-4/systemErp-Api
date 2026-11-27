using System;
using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// الخياطة والتفصيل — المرحلة 2: أدوار حسابات الوحدة الأربعة في إعدادات الحسابات (بلا قيمة؛ تُهيأ من زرها أو قبل أول ترحيل)،
    /// وأمر التفصيل بتشكيلاته وقطعه وسجل حالاته. جداول وأعمدة جديدة فقط.
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261128110000_TailoringOrders")]
    public partial class TailoringOrders : Migration
    {
        private static readonly string[] RoleColumns =
        {
            "TailoringWipAccountCode", "TailoringAbsorbedLaborAccountCode", "TailoringCostOfRevenueAccountCode", "TailoringAdvancesReceivedAccountCode",
        };

        private static OperationBuilder<AddColumnOperation> Str(ColumnsBuilder t, int max, bool required = false)
            => t.Column<string>(type: $"nvarchar({max})", maxLength: max, nullable: !required);
        private static OperationBuilder<AddColumnOperation> Money(ColumnsBuilder t)
            => t.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false);
        private static OperationBuilder<AddColumnOperation> Qty(ColumnsBuilder t, bool nullable = false)
            => t.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: nullable);
        private static OperationBuilder<AddColumnOperation> Id(ColumnsBuilder t, bool nullable = false)
            => t.Column<Guid>(type: "uniqueidentifier", nullable: nullable);
        private static OperationBuilder<AddColumnOperation> Time(ColumnsBuilder t, bool nullable = false)
            => t.Column<DateTime>(type: "datetime2", nullable: nullable);
        private static OperationBuilder<AddColumnOperation> Int(ColumnsBuilder t) => t.Column<int>(type: "int", nullable: false);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var column in RoleColumns)
                migrationBuilder.AddColumn<string>(name: column, table: "AccountingSettings", type: "nvarchar(max)", nullable: true);

            migrationBuilder.CreateTable(
                name: "TailoringOrder",
                columns: t => new
                {
                    Id = Id(t),
                    OrderNumber = Str(t, 50, true),
                    CustomerId = Id(t),
                    CustomerName = Str(t, 200, true),
                    CustomerPhone = Str(t, 30),
                    WarehouseId = Id(t, true),
                    OrderDate = Time(t),
                    ExpectedDeliveryDate = Time(t, true),
                    Priority = Str(t, 16, true),
                    Status = Str(t, 32, true),
                    Notes = Str(t, 1000),
                    PieceCount = Int(t),
                    GrossAmount = Money(t),
                    DiscountTotal = Money(t),
                    NetTotal = Money(t),
                    VatTotal = Money(t),
                    GrandTotal = Money(t),
                    AdvanceInvoiceId = Id(t, true),
                    AdvanceInvoiceNumber = Str(t, 50),
                    AdvanceAmount = Money(t),
                    AdvanceNet = Money(t),
                    AdvanceVat = Money(t),
                    AdvanceRecovered = Money(t),
                    AdvanceAt = Time(t, true),
                    ConfirmedAt = Time(t, true),
                    ConfirmedBy = Str(t, 200),
                    CancelledAt = Time(t, true),
                    CancelReason = Str(t, 500),
                    ClosedAt = Time(t, true),
                    CreatedBy = Str(t, 200),
                    CreatedByUserId = Id(t, true),
                    TenantId = Id(t),
                    CreatedAt = Time(t),
                    UpdatedAt = Time(t, true)
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_TailoringOrder", x => x.Id);
                    t.ForeignKey("FK_TailoringOrder_Customer_CustomerId", x => x.CustomerId, "Customer", "Id", onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_TailoringOrder_Warehouse_WarehouseId", x => x.WarehouseId, "Warehouse", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TailoringOrderLine",
                columns: t => new
                {
                    Id = Id(t),
                    OrderId = Id(t),
                    SortOrder = Int(t),
                    GarmentTypeId = Id(t),
                    GarmentTypeName = Str(t, 150, true),
                    ProductId = Id(t, true),
                    MeasurementProfileId = Id(t, true),
                    MeasurementVersionId = Id(t, true),
                    WearerName = Str(t, 150),
                    MeasurementUnit = Str(t, 8),
                    MeasurementsJson = t.Column<string>(type: "nvarchar(max)", nullable: true),
                    DesignJson = t.Column<string>(type: "nvarchar(max)", nullable: false),
                    FabricSource = Str(t, 16, true),
                    FabricProductId = Id(t, true),
                    FabricName = Str(t, 200),
                    FabricMeters = Qty(t),
                    FabricUnitPrice = Money(t),
                    CustomerFabricDescription = Str(t, 300),
                    CustomerFabricMeters = Qty(t, true),
                    CustomerFabricColor = Str(t, 100),
                    CustomerFabricReceivedAt = Time(t, true),
                    Quantity = Int(t),
                    BasePrice = Money(t),
                    OptionsPrice = Money(t),
                    FabricPrice = Money(t),
                    SuggestedUnitPrice = Money(t),
                    UnitPrice = Money(t),
                    PriceOverridden = t.Column<bool>(type: "bit", nullable: false),
                    DiscountAmount = Money(t),
                    NetAmount = Money(t),
                    VatRate = Money(t),
                    VatAmount = Money(t),
                    TotalAmount = Money(t),
                    Instructions = Str(t, 1000),
                    TenantId = Id(t),
                    CreatedAt = Time(t),
                    UpdatedAt = Time(t, true)
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_TailoringOrderLine", x => x.Id);
                    t.ForeignKey("FK_TailoringOrderLine_TailoringOrder_OrderId", x => x.OrderId, "TailoringOrder", "Id", onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_TailoringOrderLine_TailoringGarmentType_GarmentTypeId", x => x.GarmentTypeId, "TailoringGarmentType", "Id", onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_TailoringOrderLine_Product_ProductId", x => x.ProductId, "Product", "Id", onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_TailoringOrderLine_Product_FabricProductId", x => x.FabricProductId, "Product", "Id", onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_TailoringOrderLine_TailoringMeasurementProfile_MeasurementProfileId", x => x.MeasurementProfileId, "TailoringMeasurementProfile", "Id", onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_TailoringOrderLine_TailoringMeasurementVersion_MeasurementVersionId", x => x.MeasurementVersionId, "TailoringMeasurementVersion", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TailoringGarment",
                columns: t => new
                {
                    Id = Id(t),
                    OrderId = Id(t),
                    LineId = Id(t),
                    PieceNo = Int(t),
                    Code = Str(t, 64, true),
                    Status = Str(t, 24, true),
                    CurrentStageId = Id(t, true),
                    AssignedEmployeeId = Id(t, true),
                    ReadyAt = Time(t, true),
                    DeliveredAt = Time(t, true),
                    DeliveryId = Id(t, true),
                    TenantId = Id(t),
                    CreatedAt = Time(t),
                    UpdatedAt = Time(t, true)
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_TailoringGarment", x => x.Id);
                    t.ForeignKey("FK_TailoringGarment_TailoringOrder_OrderId", x => x.OrderId, "TailoringOrder", "Id", onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_TailoringGarment_TailoringOrderLine_LineId", x => x.LineId, "TailoringOrderLine", "Id", onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_TailoringGarment_TailoringStage_CurrentStageId", x => x.CurrentStageId, "TailoringStage", "Id", onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_TailoringGarment_Employee_AssignedEmployeeId", x => x.AssignedEmployeeId, "Employee", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TailoringOrderStatusHistory",
                columns: t => new
                {
                    Id = Id(t),
                    OrderId = Id(t),
                    FromStatus = Str(t, 32),
                    ToStatus = Str(t, 32, true),
                    Note = Str(t, 1000),
                    ChangedBy = Str(t, 200),
                    ChangedByUserId = Id(t, true),
                    ChangedAt = Time(t),
                    TenantId = Id(t),
                    CreatedAt = Time(t),
                    UpdatedAt = Time(t, true)
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_TailoringOrderStatusHistory", x => x.Id);
                    t.ForeignKey("FK_TailoringOrderStatusHistory_TailoringOrder_OrderId", x => x.OrderId, "TailoringOrder", "Id", onDelete: ReferentialAction.Restrict);
                });

            foreach (var table in Tables) migrationBuilder.CreateIndex($"IX_{table}_TenantId", table, "TenantId");
            foreach (var (table, column) in ForeignKeyIndexes) migrationBuilder.CreateIndex($"IX_{table}_{column}", table, column);
            migrationBuilder.CreateIndex("IX_TailoringOrder_TenantId_OrderNumber", "TailoringOrder", new[] { "TenantId", "OrderNumber" }, unique: true);
            migrationBuilder.CreateIndex("IX_TailoringOrder_TenantId_Status_ExpectedDeliveryDate", "TailoringOrder", new[] { "TenantId", "Status", "ExpectedDeliveryDate" });
            migrationBuilder.CreateIndex("IX_TailoringGarment_TenantId_Code", "TailoringGarment", new[] { "TenantId", "Code" }, unique: true);
            migrationBuilder.CreateIndex("IX_TailoringGarment_TenantId_Status", "TailoringGarment", new[] { "TenantId", "Status" });
        }

        private static readonly string[] Tables = { "TailoringOrder", "TailoringOrderLine", "TailoringGarment", "TailoringOrderStatusHistory" };

        private static readonly (string Table, string Column)[] ForeignKeyIndexes =
        {
            ("TailoringOrder", "CustomerId"), ("TailoringOrder", "WarehouseId"),
            ("TailoringOrderLine", "OrderId"), ("TailoringOrderLine", "GarmentTypeId"), ("TailoringOrderLine", "ProductId"), ("TailoringOrderLine", "FabricProductId"),
            ("TailoringOrderLine", "MeasurementProfileId"), ("TailoringOrderLine", "MeasurementVersionId"),
            ("TailoringGarment", "OrderId"), ("TailoringGarment", "LineId"), ("TailoringGarment", "CurrentStageId"), ("TailoringGarment", "AssignedEmployeeId"),
            ("TailoringOrderStatusHistory", "OrderId"),
        };

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "TailoringOrderStatusHistory", "TailoringGarment", "TailoringOrderLine", "TailoringOrder" })
                migrationBuilder.DropTable(table);
            foreach (var column in RoleColumns)
                migrationBuilder.DropColumn(name: column, table: "AccountingSettings");
        }
    }
}
