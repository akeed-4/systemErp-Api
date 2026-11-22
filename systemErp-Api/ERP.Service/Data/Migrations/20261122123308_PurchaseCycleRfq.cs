using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseCycleRfq : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RfqItemId",
                table: "QuotationItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeliveryDays",
                table: "Quotation",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExtraCosts",
                table: "Quotation",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "RfqId",
                table: "Quotation",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseOrderId",
                table: "DeliveryNote",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LandedUnitPrice",
                table: "CommercialOrderItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "QuotationItemId",
                table: "CommercialOrderItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequisitionItemId",
                table: "CommercialOrderItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RfqItemId",
                table: "CommercialOrderItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExtraCosts",
                table: "CommercialOrder",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "QuotationId",
                table: "CommercialOrder",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequisitionId",
                table: "CommercialOrder",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RfqId",
                table: "CommercialOrder",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PurchaseRfq",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RfqNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequisitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AwardedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    AwardedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseRfq", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseRfq_MaterialRequisition_RequisitionId",
                        column: x => x.RequisitionId,
                        principalTable: "MaterialRequisition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseRfqItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RfqId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequisitionItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseRfqItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseRfqItem_Product_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseRfqItem_PurchaseRfq_RfqId",
                        column: x => x.RfqId,
                        principalTable: "PurchaseRfq",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseRfqSupplier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RfqId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseRfqSupplier", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseRfqSupplier_PurchaseRfq_RfqId",
                        column: x => x.RfqId,
                        principalTable: "PurchaseRfq",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PurchaseRfqSupplier_Supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuotationItem_RfqItemId",
                table: "QuotationItem",
                column: "RfqItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Quotation_RfqId",
                table: "Quotation",
                column: "RfqId");

            migrationBuilder.CreateIndex(
                name: "IX_Quotation_TenantId_RfqId",
                table: "Quotation",
                columns: new[] { "TenantId", "RfqId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNote_PurchaseOrderId",
                table: "DeliveryNote",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNote_TenantId_PurchaseOrderId",
                table: "DeliveryNote",
                columns: new[] { "TenantId", "PurchaseOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOrder_QuotationId",
                table: "CommercialOrder",
                column: "QuotationId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOrder_RequisitionId",
                table: "CommercialOrder",
                column: "RequisitionId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOrder_RfqId",
                table: "CommercialOrder",
                column: "RfqId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOrder_TenantId_RequisitionId",
                table: "CommercialOrder",
                columns: new[] { "TenantId", "RequisitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOrder_TenantId_RfqId",
                table: "CommercialOrder",
                columns: new[] { "TenantId", "RfqId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRfq_RequisitionId",
                table: "PurchaseRfq",
                column: "RequisitionId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRfq_TenantId",
                table: "PurchaseRfq",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRfq_TenantId_RequisitionId_Status",
                table: "PurchaseRfq",
                columns: new[] { "TenantId", "RequisitionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRfq_TenantId_RfqNumber",
                table: "PurchaseRfq",
                columns: new[] { "TenantId", "RfqNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRfqItem_ItemId",
                table: "PurchaseRfqItem",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRfqItem_RfqId",
                table: "PurchaseRfqItem",
                column: "RfqId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRfqItem_TenantId",
                table: "PurchaseRfqItem",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRfqSupplier_RfqId",
                table: "PurchaseRfqSupplier",
                column: "RfqId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRfqSupplier_SupplierId",
                table: "PurchaseRfqSupplier",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRfqSupplier_TenantId",
                table: "PurchaseRfqSupplier",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRfqSupplier_TenantId_RfqId_SupplierId",
                table: "PurchaseRfqSupplier",
                columns: new[] { "TenantId", "RfqId", "SupplierId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CommercialOrder_MaterialRequisition_RequisitionId",
                table: "CommercialOrder",
                column: "RequisitionId",
                principalTable: "MaterialRequisition",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CommercialOrder_PurchaseRfq_RfqId",
                table: "CommercialOrder",
                column: "RfqId",
                principalTable: "PurchaseRfq",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CommercialOrder_Quotation_QuotationId",
                table: "CommercialOrder",
                column: "QuotationId",
                principalTable: "Quotation",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryNote_CommercialOrder_PurchaseOrderId",
                table: "DeliveryNote",
                column: "PurchaseOrderId",
                principalTable: "CommercialOrder",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Quotation_PurchaseRfq_RfqId",
                table: "Quotation",
                column: "RfqId",
                principalTable: "PurchaseRfq",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_QuotationItem_PurchaseRfqItem_RfqItemId",
                table: "QuotationItem",
                column: "RfqItemId",
                principalTable: "PurchaseRfqItem",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CommercialOrder_MaterialRequisition_RequisitionId",
                table: "CommercialOrder");

            migrationBuilder.DropForeignKey(
                name: "FK_CommercialOrder_PurchaseRfq_RfqId",
                table: "CommercialOrder");

            migrationBuilder.DropForeignKey(
                name: "FK_CommercialOrder_Quotation_QuotationId",
                table: "CommercialOrder");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryNote_CommercialOrder_PurchaseOrderId",
                table: "DeliveryNote");

            migrationBuilder.DropForeignKey(
                name: "FK_Quotation_PurchaseRfq_RfqId",
                table: "Quotation");

            migrationBuilder.DropForeignKey(
                name: "FK_QuotationItem_PurchaseRfqItem_RfqItemId",
                table: "QuotationItem");

            migrationBuilder.DropTable(
                name: "PurchaseRfqItem");

            migrationBuilder.DropTable(
                name: "PurchaseRfqSupplier");

            migrationBuilder.DropTable(
                name: "PurchaseRfq");

            migrationBuilder.DropIndex(
                name: "IX_QuotationItem_RfqItemId",
                table: "QuotationItem");

            migrationBuilder.DropIndex(
                name: "IX_Quotation_RfqId",
                table: "Quotation");

            migrationBuilder.DropIndex(
                name: "IX_Quotation_TenantId_RfqId",
                table: "Quotation");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryNote_PurchaseOrderId",
                table: "DeliveryNote");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryNote_TenantId_PurchaseOrderId",
                table: "DeliveryNote");

            migrationBuilder.DropIndex(
                name: "IX_CommercialOrder_QuotationId",
                table: "CommercialOrder");

            migrationBuilder.DropIndex(
                name: "IX_CommercialOrder_RequisitionId",
                table: "CommercialOrder");

            migrationBuilder.DropIndex(
                name: "IX_CommercialOrder_RfqId",
                table: "CommercialOrder");

            migrationBuilder.DropIndex(
                name: "IX_CommercialOrder_TenantId_RequisitionId",
                table: "CommercialOrder");

            migrationBuilder.DropIndex(
                name: "IX_CommercialOrder_TenantId_RfqId",
                table: "CommercialOrder");

            migrationBuilder.DropColumn(
                name: "RfqItemId",
                table: "QuotationItem");

            migrationBuilder.DropColumn(
                name: "DeliveryDays",
                table: "Quotation");

            migrationBuilder.DropColumn(
                name: "ExtraCosts",
                table: "Quotation");

            migrationBuilder.DropColumn(
                name: "RfqId",
                table: "Quotation");

            migrationBuilder.DropColumn(
                name: "PurchaseOrderId",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "LandedUnitPrice",
                table: "CommercialOrderItem");

            migrationBuilder.DropColumn(
                name: "QuotationItemId",
                table: "CommercialOrderItem");

            migrationBuilder.DropColumn(
                name: "RequisitionItemId",
                table: "CommercialOrderItem");

            migrationBuilder.DropColumn(
                name: "RfqItemId",
                table: "CommercialOrderItem");

            migrationBuilder.DropColumn(
                name: "ExtraCosts",
                table: "CommercialOrder");

            migrationBuilder.DropColumn(
                name: "QuotationId",
                table: "CommercialOrder");

            migrationBuilder.DropColumn(
                name: "RequisitionId",
                table: "CommercialOrder");

            migrationBuilder.DropColumn(
                name: "RfqId",
                table: "CommercialOrder");
        }
    }
}
