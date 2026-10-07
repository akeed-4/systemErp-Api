using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseStockAndTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "PosInvoiceSettings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "Invoice",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WarehouseName",
                table: "Invoice",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StockTransfer",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FromWarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromWarehouseName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ToWarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToWarehouseName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransfer", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockTransfer_Warehouse_FromWarehouseId",
                        column: x => x.FromWarehouseId,
                        principalTable: "Warehouse",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransfer_Warehouse_ToWarehouseId",
                        column: x => x.ToWarehouseId,
                        principalTable: "Warehouse",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WarehouseStock",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarehouseStock", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WarehouseStock_Product_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WarehouseStock_Warehouse_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouse",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockTransferItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockTransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransferItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockTransferItem_Product_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransferItem_StockTransfer_StockTransferId",
                        column: x => x.StockTransferId,
                        principalTable: "StockTransfer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PosInvoiceSettings_WarehouseId",
                table: "PosInvoiceSettings",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoice_WarehouseId",
                table: "Invoice",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfer_FromWarehouseId",
                table: "StockTransfer",
                column: "FromWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfer_TenantId",
                table: "StockTransfer",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfer_TenantId_TransferNumber",
                table: "StockTransfer",
                columns: new[] { "TenantId", "TransferNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfer_ToWarehouseId",
                table: "StockTransfer",
                column: "ToWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferItem_ItemId",
                table: "StockTransferItem",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferItem_StockTransferId",
                table: "StockTransferItem",
                column: "StockTransferId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferItem_TenantId",
                table: "StockTransferItem",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseStock_ItemId",
                table: "WarehouseStock",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseStock_TenantId",
                table: "WarehouseStock",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseStock_TenantId_ItemId_WarehouseId",
                table: "WarehouseStock",
                columns: new[] { "TenantId", "ItemId", "WarehouseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseStock_WarehouseId",
                table: "WarehouseStock",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_Invoice_Warehouse_WarehouseId",
                table: "Invoice",
                column: "WarehouseId",
                principalTable: "Warehouse",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PosInvoiceSettings_Warehouse_WarehouseId",
                table: "PosInvoiceSettings",
                column: "WarehouseId",
                principalTable: "Warehouse",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // البيانات القائمة: كل رصيد حالي يُنسب للمستودع الافتراضي للمنشأة (يُنشأ إن لم يوجد)،
            // فيبقى مجموع أرصدة المستودعات مساوياً لرصيد الصنف، والحركات القديمة بلا مستودع تُنسب إليه.
            migrationBuilder.Sql(@"
INSERT INTO [Warehouse] ([Id], [Code], [NameAr], [NameEn], [Location], [IsDefault], [Status], [TenantId], [CreatedAt])
SELECT NEWID(), 'WH-001', N'المستودع الرئيسي', 'Main Warehouse', '', 1, 'active', t.[Id], SYSUTCDATETIME()
FROM [Tenant] t
WHERE NOT EXISTS (SELECT 1 FROM [Warehouse] w WHERE w.[TenantId] = t.[Id])
  AND EXISTS (SELECT 1 FROM [Product] p WHERE p.[TenantId] = t.[Id]);

WITH DefaultWarehouse AS (
    SELECT w.[TenantId], w.[Id], ROW_NUMBER() OVER (PARTITION BY w.[TenantId] ORDER BY w.[IsDefault] DESC, w.[CreatedAt]) AS rn
    FROM [Warehouse] w
)
INSERT INTO [WarehouseStock] ([Id], [ItemId], [WarehouseId], [Quantity], [TenantId], [CreatedAt])
SELECT NEWID(), p.[Id], d.[Id], p.[CurrentStock], p.[TenantId], SYSUTCDATETIME()
FROM [Product] p
JOIN DefaultWarehouse d ON d.[TenantId] = p.[TenantId] AND d.rn = 1
WHERE p.[CurrentStock] <> 0;

WITH DefaultWarehouse AS (
    SELECT w.[TenantId], w.[Id], ROW_NUMBER() OVER (PARTITION BY w.[TenantId] ORDER BY w.[IsDefault] DESC, w.[CreatedAt]) AS rn
    FROM [Warehouse] w
)
UPDATE m SET m.[WarehouseId] = d.[Id]
FROM [StockMovement] m
JOIN DefaultWarehouse d ON d.[TenantId] = m.[TenantId] AND d.rn = 1
WHERE m.[WarehouseId] IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invoice_Warehouse_WarehouseId",
                table: "Invoice");

            migrationBuilder.DropForeignKey(
                name: "FK_PosInvoiceSettings_Warehouse_WarehouseId",
                table: "PosInvoiceSettings");

            migrationBuilder.DropTable(
                name: "StockTransferItem");

            migrationBuilder.DropTable(
                name: "WarehouseStock");

            migrationBuilder.DropTable(
                name: "StockTransfer");

            migrationBuilder.DropIndex(
                name: "IX_PosInvoiceSettings_WarehouseId",
                table: "PosInvoiceSettings");

            migrationBuilder.DropIndex(
                name: "IX_Invoice_WarehouseId",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "PosInvoiceSettings");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "WarehouseName",
                table: "Invoice");
        }
    }
}
