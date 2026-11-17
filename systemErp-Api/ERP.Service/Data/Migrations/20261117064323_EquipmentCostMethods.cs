using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class EquipmentCostMethods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EquipmentCostMethod",
                table: "ProjectResource",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CostMethod",
                table: "Equipment",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "internal_rate");

            // السلوك القائم يبقى: المملوكة بسعرها الداخلي (الافتراضي أعلاه)، والمستأجرة بإيجارها الفعلي من عقدها
            migrationBuilder.Sql("UPDATE [Equipment] SET [CostMethod] = N'actual_rental' WHERE [Ownership] = N'rented';");
            // لقطة الطريقة على موارد الإسنادات القائمة (الفارغ يعامل بسعره كما هو، فهذا للوضوح في العرض فقط)
            migrationBuilder.Sql(@"UPDATE r SET r.[EquipmentCostMethod] = e.[CostMethod]
FROM [ProjectResource] r
JOIN [EquipmentAssignment] a ON a.[ProjectResourceId] = r.[Id]
JOIN [Equipment] e ON e.[Id] = a.[EquipmentId];");

            migrationBuilder.CreateTable(
                name: "EquipmentDepreciationAllocation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FixedAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Period = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Hours = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BasisHours = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DepreciationAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentDepreciationAllocation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentDepreciationAllocation_CommercialContract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "CommercialContract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentDepreciationAllocation_Equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentDepreciationAllocation_FixedAsset_FixedAssetId",
                        column: x => x.FixedAssetId,
                        principalTable: "FixedAsset",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentDepreciationAllocation_ContractId",
                table: "EquipmentDepreciationAllocation",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentDepreciationAllocation_EquipmentId",
                table: "EquipmentDepreciationAllocation",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentDepreciationAllocation_FixedAssetId",
                table: "EquipmentDepreciationAllocation",
                column: "FixedAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentDepreciationAllocation_TenantId",
                table: "EquipmentDepreciationAllocation",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentDepreciationAllocation_TenantId_EquipmentId_Period",
                table: "EquipmentDepreciationAllocation",
                columns: new[] { "TenantId", "EquipmentId", "Period" });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentDepreciationAllocation_TenantId_FixedAssetId_Period_ContractId",
                table: "EquipmentDepreciationAllocation",
                columns: new[] { "TenantId", "FixedAssetId", "Period", "ContractId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EquipmentDepreciationAllocation");

            migrationBuilder.DropColumn(
                name: "EquipmentCostMethod",
                table: "ProjectResource");

            migrationBuilder.DropColumn(
                name: "CostMethod",
                table: "Equipment");
        }
    }
}
