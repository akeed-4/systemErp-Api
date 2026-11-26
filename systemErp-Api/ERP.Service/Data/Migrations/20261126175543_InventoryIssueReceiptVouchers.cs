using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class InventoryIssueReceiptVouchers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                table: "PurchaseLandedCostInvoice",
                type: "nvarchar(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "purchase_invoice");

            migrationBuilder.AddColumn<string>(
                name: "InventoryIssueAccountCode",
                table: "AccountingSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InventoryReceiptAccountCode",
                table: "AccountingSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InventoryVoucher",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    VoucherNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CounterAccountCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Subtotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DocumentDiscount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DiscountTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    NetTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    InventoryValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PostedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PostedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignedToUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryVoucher", x => x.Id);
                    table.CheckConstraint("CK_InventoryVouchers_Kind", "[Kind] IN ('issue', 'receipt')");
                });

            migrationBuilder.CreateTable(
                name: "InventoryVoucherLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoucherId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    UnitFactor = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false, defaultValue: 1m),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Discount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    AllocatedDiscount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ItemBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BatchNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ProductionDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StockMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryVoucherLine", x => x.Id);
                    table.CheckConstraint("CK_InventoryVoucherLines_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_InventoryVoucherLine_InventoryVoucher_VoucherId",
                        column: x => x.VoucherId,
                        principalTable: "InventoryVoucher",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InventoryVoucherLine_Product_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseLandedCostInvoice_TenantId_SourceType_InvoiceId",
                table: "PurchaseLandedCostInvoice",
                columns: new[] { "TenantId", "SourceType", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryVoucher_TenantId",
                table: "InventoryVoucher",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryVoucher_TenantId_Kind_Date",
                table: "InventoryVoucher",
                columns: new[] { "TenantId", "Kind", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryVoucher_TenantId_Kind_Status",
                table: "InventoryVoucher",
                columns: new[] { "TenantId", "Kind", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryVoucher_TenantId_VoucherNumber",
                table: "InventoryVoucher",
                columns: new[] { "TenantId", "VoucherNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryVoucherLine_ItemId",
                table: "InventoryVoucherLine",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryVoucherLine_TenantId",
                table: "InventoryVoucherLine",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryVoucherLine_TenantId_ItemId",
                table: "InventoryVoucherLine",
                columns: new[] { "TenantId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryVoucherLine_VoucherId",
                table: "InventoryVoucherLine",
                column: "VoucherId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryVoucherLine");

            migrationBuilder.DropTable(
                name: "InventoryVoucher");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseLandedCostInvoice_TenantId_SourceType_InvoiceId",
                table: "PurchaseLandedCostInvoice");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "PurchaseLandedCostInvoice");

            migrationBuilder.DropColumn(
                name: "InventoryIssueAccountCode",
                table: "AccountingSettings");

            migrationBuilder.DropColumn(
                name: "InventoryReceiptAccountCode",
                table: "AccountingSettings");
        }
    }
}
