using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProjectSupplyCycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ContractId",
                table: "MaterialRequisition",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryAddress",
                table: "MaterialRequisition",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryLocation",
                table: "MaterialRequisition",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "warehouse");

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedUnitCost",
                table: "CommercialContractItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedCost",
                table: "CommercialContract",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "ProjectMaterialIssue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssueNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReceiverType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ReceiverName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ReceiverEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReceiverSupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequisitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    TotalCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PostedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PostedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectMaterialIssue", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectMaterialIssue_CommercialContract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "CommercialContract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectMaterialIssueLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectMaterialIssueLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectMaterialIssueLine_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectMaterialIssueLine_ProjectMaterialIssue_IssueId",
                        column: x => x.IssueId,
                        principalTable: "ProjectMaterialIssue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRequisition_ContractId",
                table: "MaterialRequisition",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRequisition_TenantId_ContractId",
                table: "MaterialRequisition",
                columns: new[] { "TenantId", "ContractId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMaterialIssue_ContractId",
                table: "ProjectMaterialIssue",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMaterialIssue_TenantId",
                table: "ProjectMaterialIssue",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMaterialIssue_TenantId_ContractId_Status",
                table: "ProjectMaterialIssue",
                columns: new[] { "TenantId", "ContractId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMaterialIssue_TenantId_IssueNumber",
                table: "ProjectMaterialIssue",
                columns: new[] { "TenantId", "IssueNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMaterialIssueLine_IssueId",
                table: "ProjectMaterialIssueLine",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMaterialIssueLine_ProductId",
                table: "ProjectMaterialIssueLine",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMaterialIssueLine_TenantId",
                table: "ProjectMaterialIssueLine",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_MaterialRequisition_CommercialContract_ContractId",
                table: "MaterialRequisition",
                column: "ContractId",
                principalTable: "CommercialContract",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MaterialRequisition_CommercialContract_ContractId",
                table: "MaterialRequisition");

            migrationBuilder.DropTable(
                name: "ProjectMaterialIssueLine");

            migrationBuilder.DropTable(
                name: "ProjectMaterialIssue");

            migrationBuilder.DropIndex(
                name: "IX_MaterialRequisition_ContractId",
                table: "MaterialRequisition");

            migrationBuilder.DropIndex(
                name: "IX_MaterialRequisition_TenantId_ContractId",
                table: "MaterialRequisition");

            migrationBuilder.DropColumn(
                name: "ContractId",
                table: "MaterialRequisition");

            migrationBuilder.DropColumn(
                name: "DeliveryAddress",
                table: "MaterialRequisition");

            migrationBuilder.DropColumn(
                name: "DeliveryLocation",
                table: "MaterialRequisition");

            migrationBuilder.DropColumn(
                name: "EstimatedUnitCost",
                table: "CommercialContractItem");

            migrationBuilder.DropColumn(
                name: "EstimatedCost",
                table: "CommercialContract");
        }
    }
}
