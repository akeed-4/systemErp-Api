using System;
using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>الخياطة — المرحلة 5: التسليم بقطعه وفاتورته وقيدَي الاسترداد والتكلفة، وقيد نقل باقي التكلفة عند إغلاق الأمر.</summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261128140000_TailoringDelivery")]
    public partial class TailoringDelivery : Migration
    {
        private static OperationBuilder<AddColumnOperation> Str(ColumnsBuilder t, int max, bool required = false)
            => t.Column<string>(type: $"nvarchar({max})", maxLength: max, nullable: !required);
        private static OperationBuilder<AddColumnOperation> Id(ColumnsBuilder t, bool nullable = false) => t.Column<Guid>(type: "uniqueidentifier", nullable: nullable);
        private static OperationBuilder<AddColumnOperation> Time(ColumnsBuilder t, bool nullable = false) => t.Column<DateTime>(type: "datetime2", nullable: nullable);
        private static OperationBuilder<AddColumnOperation> Money(ColumnsBuilder t)
            => t.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TailoringDelivery",
                columns: t => new
                {
                    Id = Id(t), OrderId = Id(t), DeliveryNumber = Str(t, 64, true), Date = Time(t), Status = Str(t, 16, true),
                    PieceCount = t.Column<int>(type: "int", nullable: false), ReceivedBy = Str(t, 200), ReceiverPhone = Str(t, 50),
                    SignatureImage = t.Column<string>(type: "nvarchar(max)", nullable: true), Notes = Str(t, 1000),
                    NetAmount = Money(t), AdvanceRecovery = Money(t), TaxableAmount = Money(t), VatAmount = Money(t), TotalAmount = Money(t),
                    InvoiceId = Id(t, true), InvoiceNumber = Str(t, 64), RecoveryJournalEntryId = Id(t, true), CostAmount = Money(t), CostJournalEntryId = Id(t, true),
                    IsFinal = t.Column<bool>(type: "bit", nullable: false), CancelledAt = Time(t, true), CancelReason = Str(t, 500), CreatedBy = Str(t, 200),
                    TenantId = Id(t), CreatedAt = Time(t), UpdatedAt = Time(t, true)
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_TailoringDelivery", x => x.Id);
                    t.ForeignKey("FK_TailoringDelivery_TailoringOrder_OrderId", x => x.OrderId, "TailoringOrder", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TailoringDeliveryPiece",
                columns: t => new
                {
                    Id = Id(t), DeliveryId = Id(t), GarmentId = Id(t), LineId = Id(t), NetAmount = Money(t), AdvanceRecovery = Money(t), CostAmount = Money(t),
                    TenantId = Id(t), CreatedAt = Time(t), UpdatedAt = Time(t, true)
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_TailoringDeliveryPiece", x => x.Id);
                    t.ForeignKey("FK_TailoringDeliveryPiece_TailoringDelivery_DeliveryId", x => x.DeliveryId, "TailoringDelivery", "Id", onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_TailoringDeliveryPiece_TailoringGarment_GarmentId", x => x.GarmentId, "TailoringGarment", "Id", onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_TailoringDeliveryPiece_TailoringOrderLine_LineId", x => x.LineId, "TailoringOrderLine", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddColumn<Guid>(name: "CloseCostJournalEntryId", table: "TailoringOrder", type: "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<decimal>(name: "CloseCostAmount", table: "TailoringOrder", type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m);

            foreach (var table in new[] { "TailoringDelivery", "TailoringDeliveryPiece" })
                migrationBuilder.CreateIndex($"IX_{table}_TenantId", table, "TenantId");
            foreach (var (table, column) in new[]
            {
                ("TailoringDelivery", "OrderId"), ("TailoringDeliveryPiece", "DeliveryId"), ("TailoringDeliveryPiece", "GarmentId"), ("TailoringDeliveryPiece", "LineId"),
                ("TailoringGarment", "DeliveryId"),
            })
                migrationBuilder.CreateIndex($"IX_{table}_{column}", table, column);
            migrationBuilder.CreateIndex("IX_TailoringDelivery_TenantId_DeliveryNumber", "TailoringDelivery", new[] { "TenantId", "DeliveryNumber" }, unique: true);
            migrationBuilder.CreateIndex("IX_TailoringDelivery_TenantId_OrderId", "TailoringDelivery", new[] { "TenantId", "OrderId" });
            migrationBuilder.CreateIndex("IX_TailoringDelivery_TenantId_Date", "TailoringDelivery", new[] { "TenantId", "Date" });
            // تقارير الفترة (المرحلة 5): مدد المراحل والإنتاجية، واستهلاك المواد
            migrationBuilder.CreateIndex("IX_TailoringStageHistory_TenantId_ChangedAt", "TailoringStageHistory", new[] { "TenantId", "ChangedAt" });
            migrationBuilder.CreateIndex("IX_TailoringMaterialIssue_TenantId_Date", "TailoringMaterialIssue", new[] { "TenantId", "Date" });
            migrationBuilder.AddForeignKey("FK_TailoringGarment_TailoringDelivery_DeliveryId", "TailoringGarment", "DeliveryId", "TailoringDelivery",
                principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey("FK_TailoringGarment_TailoringDelivery_DeliveryId", "TailoringGarment");
            migrationBuilder.DropIndex("IX_TailoringGarment_DeliveryId", "TailoringGarment");
            migrationBuilder.DropIndex("IX_TailoringStageHistory_TenantId_ChangedAt", "TailoringStageHistory");
            migrationBuilder.DropIndex("IX_TailoringMaterialIssue_TenantId_Date", "TailoringMaterialIssue");
            migrationBuilder.DropColumn("CloseCostJournalEntryId", "TailoringOrder");
            migrationBuilder.DropColumn("CloseCostAmount", "TailoringOrder");
            foreach (var table in new[] { "TailoringDeliveryPiece", "TailoringDelivery" })
                migrationBuilder.DropTable(table);
        }
    }
}
