using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExecutionPlanIndependentBudget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "StudyCost",
                table: "ProjectResourceRequirement",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StudyHoursPerUnit",
                table: "ProjectResourceRequirement",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StudyQuantity",
                table: "ProjectResourceRequirement",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StudyUnitCost",
                table: "ProjectResourceRequirement",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProjectPlanChange",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    ItemKind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ItemName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Field = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    StudyValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OldValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CostDelta = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
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
                    table.PrimaryKey("PK_ProjectPlanChange", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPlanChange_TenantId",
                table: "ProjectPlanChange",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPlanChange_TenantId_ContractId_ChangedAt",
                table: "ProjectPlanChange",
                columns: new[] { "TenantId", "ContractId", "ChangedAt" });

            // الخطط القائمة: حقول الأسطر غير اليدوية كانت مقفلة على قيم الدراسة، فقيمها الحالية هي قيم الدراسة (مرجع المقارنة)
            migrationBuilder.Sql(@"UPDATE [ProjectResourceRequirement]
SET [StudyQuantity] = [Quantity], [StudyHoursPerUnit] = [HoursPerUnit], [StudyUnitCost] = [UnitCost], [StudyCost] = [PlannedCost]
WHERE [Source] <> N'manual' AND [StudyQuantity] IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectPlanChange");

            migrationBuilder.DropColumn(
                name: "StudyCost",
                table: "ProjectResourceRequirement");

            migrationBuilder.DropColumn(
                name: "StudyHoursPerUnit",
                table: "ProjectResourceRequirement");

            migrationBuilder.DropColumn(
                name: "StudyQuantity",
                table: "ProjectResourceRequirement");

            migrationBuilder.DropColumn(
                name: "StudyUnitCost",
                table: "ProjectResourceRequirement");
        }
    }
}
