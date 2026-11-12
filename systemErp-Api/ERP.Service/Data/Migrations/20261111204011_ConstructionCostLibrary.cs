using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConstructionCostLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "ConstructionResource",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CostEffectiveFrom",
                table: "ConstructionResource",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EquipmentOwnership",
                table: "ConstructionResource",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupplierId",
                table: "ConstructionResource",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConstructionResourceCost",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    HoursPerUnit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RateBasis = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Rate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    HoursPerDay = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DaysPerMonth = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    FuelPerHour = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    MaintenancePerHour = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DepreciationPerHour = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OperatorPerHour = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OtherPerHour = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OvertimeMultiplier = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    HourlyCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OvertimeHourlyCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConstructionResourceCost", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConstructionResourceCost_ConstructionResource_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "ConstructionResource",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConstructionResource_SupplierId",
                table: "ConstructionResource",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_ConstructionResourceCost_ResourceId",
                table: "ConstructionResourceCost",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ConstructionResourceCost_TenantId",
                table: "ConstructionResourceCost",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ConstructionResourceCost_TenantId_ResourceId_EffectiveFrom",
                table: "ConstructionResourceCost",
                columns: new[] { "TenantId", "ResourceId", "EffectiveFrom" });

            migrationBuilder.AddForeignKey(
                name: "FK_ConstructionResource_Supplier_SupplierId",
                table: "ConstructionResource",
                column: "SupplierId",
                principalTable: "Supplier",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // سطر افتتاحي في السجل لكل مورد قائم بتكلفته الحالية ومصدره ووحدته، سارياً من آخر ضبط لتكلفته (أو إنشائه)،
            // بعملة المنشأة؛ والتكلفة المخزّنة تأخذ تاريخ سريانه. لا تُمس أي قيمة تكلفة، ولا عقد ولا مشروع.
            migrationBuilder.Sql(@"
INSERT INTO [ConstructionResourceCost]
    ([Id], [TenantId], [CreatedAt], [ResourceId], [EffectiveFrom], [UnitCost], [Currency], [Source], [Unit], [HoursPerUnit],
     [Rate], [HoursPerDay], [DaysPerMonth], [FuelPerHour], [MaintenancePerHour], [DepreciationPerHour], [OperatorPerHour], [OtherPerHour],
     [OvertimeMultiplier], [HourlyCost], [OvertimeHourlyCost], [Reason])
SELECT NEWID(), r.[TenantId], SYSUTCDATETIME(), r.[Id], CAST(CAST(COALESCE(r.[CostUpdatedAt], r.[CreatedAt]) AS date) AS datetime2),
       r.[StandardCost], COALESCE(NULLIF(t.[Currency], N''), N'SAR'), r.[CostSource], r.[Unit], r.[HoursPerUnit],
       0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, N'migration'
FROM [ConstructionResource] r
LEFT JOIN [Tenant] t ON t.[Id] = r.[TenantId];

UPDATE [ConstructionResource]
SET [CostEffectiveFrom] = CAST(CAST(COALESCE([CostUpdatedAt], [CreatedAt]) AS date) AS datetime2);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ConstructionResource_Supplier_SupplierId",
                table: "ConstructionResource");

            migrationBuilder.DropTable(
                name: "ConstructionResourceCost");

            migrationBuilder.DropIndex(
                name: "IX_ConstructionResource_SupplierId",
                table: "ConstructionResource");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "ConstructionResource");

            migrationBuilder.DropColumn(
                name: "CostEffectiveFrom",
                table: "ConstructionResource");

            migrationBuilder.DropColumn(
                name: "EquipmentOwnership",
                table: "ConstructionResource");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "ConstructionResource");
        }
    }
}
