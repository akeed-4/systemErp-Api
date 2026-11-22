using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// حسابا عجز وزيادة المخزون لكل مستودع (فروق الجرد المستمر)، مع تكلفة إيراد خاصة للمستودع الرئيسي. تُنشأ للمستودع الجديد تلقائياً،
    /// وللقائمة عبر <c>POST api/v1/warehouses/site-accounts</c> (زر «إنشاء حسابات المواقع»).
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261122235000_WarehouseVarianceAccounts")]
    public partial class WarehouseVarianceAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "ShortageAccountCode", table: "Warehouse", type: "nvarchar(max)", nullable: true);
            migrationBuilder.AddColumn<string>(name: "SurplusAccountCode", table: "Warehouse", type: "nvarchar(max)", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ShortageAccountCode", table: "Warehouse");
            migrationBuilder.DropColumn(name: "SurplusAccountCode", table: "Warehouse");
        }
    }
}
