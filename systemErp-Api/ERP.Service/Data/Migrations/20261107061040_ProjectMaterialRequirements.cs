using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProjectMaterialRequirements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WorkSubItemId",
                table: "CommercialContractItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProjectMaterialPlan",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReturnNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectMaterialPlan", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectMaterialPlan_CommercialContract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "CommercialContract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectMaterialRequirement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BoqItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BaselineQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectMaterialRequirement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectMaterialRequirement_CommercialContract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "CommercialContract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectMaterialRequirement_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectMaterialRequirement_ProjectMaterialPlan_PlanId",
                        column: x => x.PlanId,
                        principalTable: "ProjectMaterialPlan",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMaterialPlan_ContractId",
                table: "ProjectMaterialPlan",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMaterialPlan_TenantId",
                table: "ProjectMaterialPlan",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMaterialPlan_TenantId_ContractId",
                table: "ProjectMaterialPlan",
                columns: new[] { "TenantId", "ContractId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMaterialRequirement_ContractId",
                table: "ProjectMaterialRequirement",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMaterialRequirement_PlanId",
                table: "ProjectMaterialRequirement",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMaterialRequirement_ProductId",
                table: "ProjectMaterialRequirement",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMaterialRequirement_TenantId",
                table: "ProjectMaterialRequirement",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMaterialRequirement_TenantId_ContractId_ProductId",
                table: "ProjectMaterialRequirement",
                columns: new[] { "TenantId", "ContractId", "ProductId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectMaterialRequirement");

            migrationBuilder.DropTable(
                name: "ProjectMaterialPlan");

            migrationBuilder.DropColumn(
                name: "WorkSubItemId",
                table: "CommercialContractItem");
        }
    }
}
