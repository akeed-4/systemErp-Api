using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// بضاعة أول المدة وآخرها لكل موقع إضافي (<c>SiteAccounts</c>): تُنشآن تلقائياً للموقع الجديد، وللقائم بزر «إنشاء حسابات المواقع»
    /// (<c>POST api/v1/warehouses/site-accounts</c>). الرئيسي على حسابي الإعدادات. أعمدة فقط.
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261124130000_WarehousePeriodicInventoryAccounts")]
    public partial class WarehousePeriodicInventoryAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "OpeningInventoryAccountCode", table: "Warehouse", type: "nvarchar(max)", nullable: true);
            migrationBuilder.AddColumn<string>(name: "EndingInventoryAccountCode", table: "Warehouse", type: "nvarchar(max)", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "OpeningInventoryAccountCode", table: "Warehouse");
            migrationBuilder.DropColumn(name: "EndingInventoryAccountCode", table: "Warehouse");
        }
    }
}
