using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// وحدات الصنف البديلة ووحدة السطر:
    /// - جدول <c>ProductUnit</c>: وحدات بديلة مرتبطة بالصنف بمعامل تحويلها إلى وحدته الأساسية، و<c>Product.SalesUnit</c> وحدة البيع الافتراضية.
    /// - <c>UnitFactor</c> على أسطر الفواتير والمرتجعات (كل جداول TPC) بقيمة 1 للقائم: كل سطر قديم بالوحدة الأساسية، فلا تتغير أي
    ///   فاتورة ولا رصيد ولا تُرحَّل بيانات.
    /// </summary>
    public partial class ProductUnitsAndLineUnitFactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "UnitFactor",
                table: "SalesReturnItem",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitFactor",
                table: "SalesInvoiceItem",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitFactor",
                table: "PurchaseReturnItem",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitFactor",
                table: "PurchaseInvoiceItem",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "SalesUnit",
                table: "Product",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitFactor",
                table: "InvoiceItem",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.CreateTable(
                name: "ProductUnit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Factor = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductUnit", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductUnit_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductUnit_ProductId",
                table: "ProductUnit",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductUnit_TenantId",
                table: "ProductUnit",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductUnit");

            migrationBuilder.DropColumn(
                name: "UnitFactor",
                table: "SalesReturnItem");

            migrationBuilder.DropColumn(
                name: "UnitFactor",
                table: "SalesInvoiceItem");

            migrationBuilder.DropColumn(
                name: "UnitFactor",
                table: "PurchaseReturnItem");

            migrationBuilder.DropColumn(
                name: "UnitFactor",
                table: "PurchaseInvoiceItem");

            migrationBuilder.DropColumn(
                name: "SalesUnit",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "UnitFactor",
                table: "InvoiceItem");
        }
    }
}
