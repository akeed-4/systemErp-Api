using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// حسابات الموقع الخاصة: مبيعاته وتكلفة إيراده وصندوقه (مع حساب مخزونه القائم). تُنشأ تلقائياً للموقع الإضافي، وللمواقع القائمة عبر
    /// <c>POST api/v1/warehouses/site-accounts</c>.
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261122200000_WarehouseSiteAccounts")]
    public partial class WarehouseSiteAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "SalesAccountCode", table: "Warehouse", type: "nvarchar(max)", nullable: true);
            migrationBuilder.AddColumn<string>(name: "CogsAccountCode", table: "Warehouse", type: "nvarchar(max)", nullable: true);
            migrationBuilder.AddColumn<string>(name: "CashAccountCode", table: "Warehouse", type: "nvarchar(max)", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SalesAccountCode", table: "Warehouse");
            migrationBuilder.DropColumn(name: "CogsAccountCode", table: "Warehouse");
            migrationBuilder.DropColumn(name: "CashAccountCode", table: "Warehouse");
        }
    }
}
