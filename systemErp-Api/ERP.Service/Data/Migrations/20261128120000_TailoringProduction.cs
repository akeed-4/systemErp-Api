using System;
using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>الخياطة — المرحلة 3: بداية مرحلة القطعة، وسجل المراحل، والبروفات، وطلبات التعديل. جداول وعمود جديد فقط.</summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261128120000_TailoringProduction")]
    public partial class TailoringProduction : Migration
    {
        private static OperationBuilder<AddColumnOperation> Str(ColumnsBuilder t, int max, bool required = false)
            => t.Column<string>(type: $"nvarchar({max})", maxLength: max, nullable: !required);
        private static OperationBuilder<AddColumnOperation> Id(ColumnsBuilder t, bool nullable = false) => t.Column<Guid>(type: "uniqueidentifier", nullable: nullable);
        private static OperationBuilder<AddColumnOperation> Time(ColumnsBuilder t, bool nullable = false) => t.Column<DateTime>(type: "datetime2", nullable: nullable);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(name: "StageStartedAt", table: "TailoringGarment", type: "datetime2", nullable: true);

            migrationBuilder.CreateTable(
                name: "TailoringStageHistory",
                columns: t => new
                {
                    Id = Id(t), OrderId = Id(t), GarmentId = Id(t), FromStageId = Id(t, true), ToStageId = Id(t, true),
                    FromStatus = Str(t, 24), ToStatus = Str(t, 24, true), EmployeeId = Id(t, true),
                    DurationMinutes = t.Column<int>(type: "int", nullable: false), Note = Str(t, 1000), ChangedBy = Str(t, 200),
                    ChangedByUserId = Id(t, true), ChangedAt = Time(t), TenantId = Id(t), CreatedAt = Time(t), UpdatedAt = Time(t, true)
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_TailoringStageHistory", x => x.Id);
                    t.ForeignKey("FK_TailoringStageHistory_TailoringOrder_OrderId", x => x.OrderId, "TailoringOrder", "Id", onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_TailoringStageHistory_TailoringGarment_GarmentId", x => x.GarmentId, "TailoringGarment", "Id", onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_TailoringStageHistory_Employee_EmployeeId", x => x.EmployeeId, "Employee", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TailoringFitting",
                columns: t => new
                {
                    Id = Id(t), OrderId = Id(t), GarmentId = Id(t), ScheduledAt = Time(t), Status = Str(t, 16, true), Result = Str(t, 16),
                    Notes = Str(t, 1000), DoneAt = Time(t, true), CreatedBy = Str(t, 200), TenantId = Id(t), CreatedAt = Time(t), UpdatedAt = Time(t, true)
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_TailoringFitting", x => x.Id);
                    t.ForeignKey("FK_TailoringFitting_TailoringOrder_OrderId", x => x.OrderId, "TailoringOrder", "Id", onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_TailoringFitting_TailoringGarment_GarmentId", x => x.GarmentId, "TailoringGarment", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TailoringModification",
                columns: t => new
                {
                    Id = Id(t), OrderId = Id(t), GarmentId = Id(t), FittingId = Id(t, true), Instructions = Str(t, 2000, true),
                    AssignedEmployeeId = Id(t, true), Status = Str(t, 16, true), DoneAt = Time(t, true), CreatedBy = Str(t, 200),
                    TenantId = Id(t), CreatedAt = Time(t), UpdatedAt = Time(t, true)
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_TailoringModification", x => x.Id);
                    t.ForeignKey("FK_TailoringModification_TailoringOrder_OrderId", x => x.OrderId, "TailoringOrder", "Id", onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_TailoringModification_TailoringGarment_GarmentId", x => x.GarmentId, "TailoringGarment", "Id", onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_TailoringModification_TailoringFitting_FittingId", x => x.FittingId, "TailoringFitting", "Id", onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_TailoringModification_Employee_AssignedEmployeeId", x => x.AssignedEmployeeId, "Employee", "Id", onDelete: ReferentialAction.Restrict);
                });

            foreach (var table in new[] { "TailoringStageHistory", "TailoringFitting", "TailoringModification" })
                migrationBuilder.CreateIndex($"IX_{table}_TenantId", table, "TenantId");
            foreach (var (table, column) in new[]
            {
                ("TailoringStageHistory", "OrderId"), ("TailoringStageHistory", "GarmentId"), ("TailoringStageHistory", "EmployeeId"),
                ("TailoringFitting", "OrderId"), ("TailoringFitting", "GarmentId"),
                ("TailoringModification", "OrderId"), ("TailoringModification", "GarmentId"), ("TailoringModification", "FittingId"),
                ("TailoringModification", "AssignedEmployeeId"),
            })
                migrationBuilder.CreateIndex($"IX_{table}_{column}", table, column);
            migrationBuilder.CreateIndex("IX_TailoringFitting_TenantId_Status_ScheduledAt", "TailoringFitting", new[] { "TenantId", "Status", "ScheduledAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "TailoringModification", "TailoringFitting", "TailoringStageHistory" })
                migrationBuilder.DropTable(table);
            migrationBuilder.DropColumn(name: "StageStartedAt", table: "TailoringGarment");
        }
    }
}
