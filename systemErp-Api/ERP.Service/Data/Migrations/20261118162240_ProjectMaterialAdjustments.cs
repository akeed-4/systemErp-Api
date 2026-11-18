using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProjectMaterialAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectMaterialAdjustment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VariationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VariationNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecidedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectMaterialAdjustment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectMaterialAdjustment_CommercialContract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "CommercialContract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectMaterialAdjustmentLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdjustmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequirementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BoqItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ItemQuantityChange = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    QuantityPerUnit = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ProposedQuantityChange = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    QuantityChange = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectMaterialAdjustmentLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectMaterialAdjustmentLine_ConstructionResource_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "ConstructionResource",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectMaterialAdjustmentLine_ProjectMaterialAdjustment_AdjustmentId",
                        column: x => x.AdjustmentId,
                        principalTable: "ProjectMaterialAdjustment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMaterialAdjustment_ContractId",
                table: "ProjectMaterialAdjustment",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMaterialAdjustment_TenantId",
                table: "ProjectMaterialAdjustment",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMaterialAdjustment_TenantId_ContractId_Status",
                table: "ProjectMaterialAdjustment",
                columns: new[] { "TenantId", "ContractId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMaterialAdjustment_TenantId_VariationId",
                table: "ProjectMaterialAdjustment",
                columns: new[] { "TenantId", "VariationId" },
                unique: true,
                filter: "[VariationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMaterialAdjustmentLine_AdjustmentId",
                table: "ProjectMaterialAdjustmentLine",
                column: "AdjustmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMaterialAdjustmentLine_ResourceId",
                table: "ProjectMaterialAdjustmentLine",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMaterialAdjustmentLine_TenantId",
                table: "ProjectMaterialAdjustmentLine",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMaterialAdjustmentLine_TenantId_RequirementId",
                table: "ProjectMaterialAdjustmentLine",
                columns: new[] { "TenantId", "RequirementId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectMaterialAdjustmentLine");

            migrationBuilder.DropTable(
                name: "ProjectMaterialAdjustment");
        }
    }
}
