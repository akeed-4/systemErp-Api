using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// مخزون السيارات في حساب موقعها: حساب مخزون الموقع المحجوزة فيه تكلفة السيارة والمبلغ المحجوز (VehicleSiteLedger). السيارات القائمة
    /// تُزامَن عبر <c>POST api/v1/vehicles/site-inventory/sync</c> أو تلقائياً عند بيعها أو تحويلها.
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261122210000_VehicleSiteInventory")]
    public partial class VehicleSiteInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "SiteInventoryAccountCode", table: "Vehicle", type: "nvarchar(max)", nullable: true);
            migrationBuilder.AddColumn<decimal>(name: "SiteBookedCost", table: "Vehicle", type: "decimal(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SiteInventoryAccountCode", table: "Vehicle");
            migrationBuilder.DropColumn(name: "SiteBookedCost", table: "Vehicle");
        }
    }
}
