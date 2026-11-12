using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ContractItemCostAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContractItemResourceCost",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WorkSubItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResourceType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ResourceCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ResourceName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    HoursPerUnit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BaseQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    QuantityPerUnit = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    StandardUnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CostId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CostEffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CostSource = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CostStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OverrideReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    InputsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PricingDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PricedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PricedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PricedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractItemResourceCost", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContractItemResourceCost_TenantId",
                table: "ContractItemResourceCost",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractItemResourceCost_TenantId_ContractId_ContractItemId",
                table: "ContractItemResourceCost",
                columns: new[] { "TenantId", "ContractId", "ContractItemId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContractItemResourceCost");
        }
    }
}
