using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// حالة تسليم فاتورة البيع: «غير مسلّمة» (DeliveryDeferred) لا تصرف المخزون عند الترحيل، وبيانات التسليم منها تصرفه من موقع كل سطر
    /// بتكلفته (StockIssuedQty/StockIssuedValue على سطر البيان أساس المزامنة والعكس). الفواتير والبيانات القائمة: «مسلّمة» وصفر، كما كانت.
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261128180000_InvoiceDeliveryDeferred")]
    public partial class InvoiceDeliveryDeferred : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DeliveryDeferred", table: "SalesInvoice", type: "bit", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<bool>(
                name: "DeliveryDeferred", table: "PurchaseInvoice", type: "bit", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<decimal>(
                name: "StockIssuedQty", table: "DeliveryNoteItem", type: "decimal(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(
                name: "StockIssuedValue", table: "DeliveryNoteItem", type: "decimal(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DeliveryDeferred", table: "SalesInvoice");
            migrationBuilder.DropColumn(name: "DeliveryDeferred", table: "PurchaseInvoice");
            migrationBuilder.DropColumn(name: "StockIssuedQty", table: "DeliveryNoteItem");
            migrationBuilder.DropColumn(name: "StockIssuedValue", table: "DeliveryNoteItem");
        }
    }
}
