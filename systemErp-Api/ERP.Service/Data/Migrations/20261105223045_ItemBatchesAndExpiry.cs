using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ItemBatchesAndExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BatchNumber",
                table: "StockTransferItem",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiryDate",
                table: "StockTransferItem",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ItemBatchId",
                table: "StockTransferItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BatchSummary",
                table: "StockMovement",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BatchNumber",
                table: "SalesReturnItem",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiryDate",
                table: "SalesReturnItem",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ItemBatchId",
                table: "SalesReturnItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProductionDate",
                table: "SalesReturnItem",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BatchNumber",
                table: "SalesInvoiceItem",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiryDate",
                table: "SalesInvoiceItem",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ItemBatchId",
                table: "SalesInvoiceItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProductionDate",
                table: "SalesInvoiceItem",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BatchNumber",
                table: "PurchaseReturnItem",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiryDate",
                table: "PurchaseReturnItem",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ItemBatchId",
                table: "PurchaseReturnItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProductionDate",
                table: "PurchaseReturnItem",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BatchNumber",
                table: "PurchaseInvoiceItem",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiryDate",
                table: "PurchaseInvoiceItem",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ItemBatchId",
                table: "PurchaseInvoiceItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProductionDate",
                table: "PurchaseInvoiceItem",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExpiryAlertDays",
                table: "Product",
                type: "int",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "ShelfLifeDays",
                table: "Product",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TrackBatches",
                table: "Product",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TrackExpiry",
                table: "Product",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BatchNumber",
                table: "InvoiceItem",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiryDate",
                table: "InvoiceItem",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ItemBatchId",
                table: "InvoiceItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProductionDate",
                table: "InvoiceItem",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BatchNumber",
                table: "InventoryCountLine",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiryDate",
                table: "InventoryCountLine",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ItemBatchId",
                table: "InventoryCountLine",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ItemBatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProductionDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemBatch", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemBatch_Product_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BatchStock",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BatchStock", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BatchStock_ItemBatch_ItemBatchId",
                        column: x => x.ItemBatchId,
                        principalTable: "ItemBatch",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BatchStock_Product_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BatchStock_Warehouse_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouse",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockMovementBatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsReversed = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovementBatch", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockMovementBatch_ItemBatch_ItemBatchId",
                        column: x => x.ItemBatchId,
                        principalTable: "ItemBatch",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockMovementBatch_StockMovement_StockMovementId",
                        column: x => x.StockMovementId,
                        principalTable: "StockMovement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BatchStock_ItemBatchId",
                table: "BatchStock",
                column: "ItemBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_BatchStock_ItemId",
                table: "BatchStock",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_BatchStock_TenantId",
                table: "BatchStock",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BatchStock_TenantId_ItemBatchId_WarehouseId",
                table: "BatchStock",
                columns: new[] { "TenantId", "ItemBatchId", "WarehouseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BatchStock_TenantId_ItemId_WarehouseId",
                table: "BatchStock",
                columns: new[] { "TenantId", "ItemId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_BatchStock_WarehouseId",
                table: "BatchStock",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemBatch_ItemId",
                table: "ItemBatch",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemBatch_TenantId",
                table: "ItemBatch",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemBatch_TenantId_ExpiryDate",
                table: "ItemBatch",
                columns: new[] { "TenantId", "ExpiryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemBatch_TenantId_ItemId_BatchNumber",
                table: "ItemBatch",
                columns: new[] { "TenantId", "ItemId", "BatchNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockMovementBatch_ItemBatchId",
                table: "StockMovementBatch",
                column: "ItemBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovementBatch_StockMovementId",
                table: "StockMovementBatch",
                column: "StockMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovementBatch_TenantId",
                table: "StockMovementBatch",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovementBatch_TenantId_SourceType_SourceId",
                table: "StockMovementBatch",
                columns: new[] { "TenantId", "SourceType", "SourceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BatchStock");

            migrationBuilder.DropTable(
                name: "StockMovementBatch");

            migrationBuilder.DropTable(
                name: "ItemBatch");

            migrationBuilder.DropColumn(
                name: "BatchNumber",
                table: "StockTransferItem");

            migrationBuilder.DropColumn(
                name: "ExpiryDate",
                table: "StockTransferItem");

            migrationBuilder.DropColumn(
                name: "ItemBatchId",
                table: "StockTransferItem");

            migrationBuilder.DropColumn(
                name: "BatchSummary",
                table: "StockMovement");

            migrationBuilder.DropColumn(
                name: "BatchNumber",
                table: "SalesReturnItem");

            migrationBuilder.DropColumn(
                name: "ExpiryDate",
                table: "SalesReturnItem");

            migrationBuilder.DropColumn(
                name: "ItemBatchId",
                table: "SalesReturnItem");

            migrationBuilder.DropColumn(
                name: "ProductionDate",
                table: "SalesReturnItem");

            migrationBuilder.DropColumn(
                name: "BatchNumber",
                table: "SalesInvoiceItem");

            migrationBuilder.DropColumn(
                name: "ExpiryDate",
                table: "SalesInvoiceItem");

            migrationBuilder.DropColumn(
                name: "ItemBatchId",
                table: "SalesInvoiceItem");

            migrationBuilder.DropColumn(
                name: "ProductionDate",
                table: "SalesInvoiceItem");

            migrationBuilder.DropColumn(
                name: "BatchNumber",
                table: "PurchaseReturnItem");

            migrationBuilder.DropColumn(
                name: "ExpiryDate",
                table: "PurchaseReturnItem");

            migrationBuilder.DropColumn(
                name: "ItemBatchId",
                table: "PurchaseReturnItem");

            migrationBuilder.DropColumn(
                name: "ProductionDate",
                table: "PurchaseReturnItem");

            migrationBuilder.DropColumn(
                name: "BatchNumber",
                table: "PurchaseInvoiceItem");

            migrationBuilder.DropColumn(
                name: "ExpiryDate",
                table: "PurchaseInvoiceItem");

            migrationBuilder.DropColumn(
                name: "ItemBatchId",
                table: "PurchaseInvoiceItem");

            migrationBuilder.DropColumn(
                name: "ProductionDate",
                table: "PurchaseInvoiceItem");

            migrationBuilder.DropColumn(
                name: "ExpiryAlertDays",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "ShelfLifeDays",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "TrackBatches",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "TrackExpiry",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "BatchNumber",
                table: "InvoiceItem");

            migrationBuilder.DropColumn(
                name: "ExpiryDate",
                table: "InvoiceItem");

            migrationBuilder.DropColumn(
                name: "ItemBatchId",
                table: "InvoiceItem");

            migrationBuilder.DropColumn(
                name: "ProductionDate",
                table: "InvoiceItem");

            migrationBuilder.DropColumn(
                name: "BatchNumber",
                table: "InventoryCountLine");

            migrationBuilder.DropColumn(
                name: "ExpiryDate",
                table: "InventoryCountLine");

            migrationBuilder.DropColumn(
                name: "ItemBatchId",
                table: "InventoryCountLine");
        }
    }
}
