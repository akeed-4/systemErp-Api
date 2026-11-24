using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// تسعير الأصناف ومستودع لكل سطر:
    /// - <c>AccountingSettings.ItemPricesIncludeVat</c>: سعر بطاقة الصنف شامل الضريبة (معطّل افتراضياً = السلوك القائم).
    /// - <c>WarehouseId</c> على أسطر الفواتير والمرتجعات (كل جداول TPC) وعروض الأسعار والأوامر وبيانات التسليم: الفارغ = موقع المستند،
    ///   فلا تتغير أي وثيقة قائمة ولا تُرحَّل بيانات.
    /// - <c>CostCenterId</c> على أسطر عروض الأسعار والأوامر وبيانات التسليم، و<c>Discount</c> على سطر بيان التسليم (صفر للقائم).
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261124160000_LineWarehouseAndItemPricing")]
    public partial class LineWarehouseAndItemPricing : Migration
    {
        /// <summary>جداول أسطر الفواتير (TPC: جدول لكل نوع مستند).</summary>
        private static readonly string[] InvoiceLineTables =
            { "SalesInvoiceItem", "PurchaseInvoiceItem", "SalesReturnItem", "PurchaseReturnItem", "InvoiceItem" };

        /// <summary>أسطر المستندات التي تُحوَّل إلى فاتورة.</summary>
        private static readonly string[] SourceLineTables = { "QuotationItem", "CommercialOrderItem", "DeliveryNoteItem" };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(name: "ItemPricesIncludeVat", table: "AccountingSettings", type: "bit", nullable: false, defaultValue: false);

            foreach (var table in InvoiceLineTables) AddWarehouse(migrationBuilder, table);
            foreach (var table in SourceLineTables)
            {
                AddWarehouse(migrationBuilder, table);
                migrationBuilder.AddColumn<Guid>(name: "CostCenterId", table: table, type: "uniqueidentifier", nullable: true);
                migrationBuilder.CreateIndex(name: $"IX_{table}_CostCenterId", table: table, column: "CostCenterId");
                migrationBuilder.AddForeignKey(name: $"FK_{table}_CostCenter_CostCenterId", table: table, column: "CostCenterId",
                    principalTable: "CostCenter", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            }

            migrationBuilder.AddColumn<decimal>(name: "Discount", table: "DeliveryNoteItem", type: "decimal(18,4)", precision: 18, scale: 4,
                nullable: false, defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Discount", table: "DeliveryNoteItem");
            foreach (var table in SourceLineTables)
            {
                migrationBuilder.DropForeignKey(name: $"FK_{table}_CostCenter_CostCenterId", table: table);
                migrationBuilder.DropIndex(name: $"IX_{table}_CostCenterId", table: table);
                migrationBuilder.DropColumn(name: "CostCenterId", table: table);
                DropWarehouse(migrationBuilder, table);
            }
            foreach (var table in InvoiceLineTables) DropWarehouse(migrationBuilder, table);
            migrationBuilder.DropColumn(name: "ItemPricesIncludeVat", table: "AccountingSettings");
        }

        private static void AddWarehouse(MigrationBuilder migrationBuilder, string table)
        {
            migrationBuilder.AddColumn<Guid>(name: "WarehouseId", table: table, type: "uniqueidentifier", nullable: true);
            migrationBuilder.CreateIndex(name: $"IX_{table}_WarehouseId", table: table, column: "WarehouseId");
            migrationBuilder.AddForeignKey(name: $"FK_{table}_Warehouse_WarehouseId", table: table, column: "WarehouseId",
                principalTable: "Warehouse", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        }

        private static void DropWarehouse(MigrationBuilder migrationBuilder, string table)
        {
            migrationBuilder.DropForeignKey(name: $"FK_{table}_Warehouse_WarehouseId", table: table);
            migrationBuilder.DropIndex(name: $"IX_{table}_WarehouseId", table: table);
            migrationBuilder.DropColumn(name: "WarehouseId", table: table);
        }
    }
}
