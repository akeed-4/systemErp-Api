using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleWarehouse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "Vehicle",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WarehouseName",
                table: "Vehicle",
                type: "nvarchar(max)",
                nullable: true);

            // السيارات الحالية: معرض فاتورة شرائها، ثم معرض أمر الشراء، ثم المعرض الافتراضي للمنشأة
            migrationBuilder.Sql(@"
UPDATE v SET v.[WarehouseId] = i.[WarehouseId], v.[WarehouseName] = i.[WarehouseName]
FROM [Vehicle] v INNER JOIN [Invoice] i ON i.[Id] = v.[PurchaseInvoiceId]
WHERE v.[WarehouseId] IS NULL AND i.[WarehouseId] IS NOT NULL;

UPDATE v SET v.[WarehouseId] = w.[Id], v.[WarehouseName] = w.[NameAr]
FROM [Vehicle] v
INNER JOIN [CarProcurementOrder] o ON o.[Id] = v.[ProcurementOrderId]
INNER JOIN [Warehouse] w ON w.[Id] = o.[WarehouseId]
WHERE v.[WarehouseId] IS NULL;

UPDATE v SET v.[WarehouseId] = w.[Id], v.[WarehouseName] = w.[NameAr]
FROM [Vehicle] v
CROSS APPLY (SELECT TOP 1 x.[Id], x.[NameAr] FROM [Warehouse] x WHERE x.[TenantId] = v.[TenantId] ORDER BY x.[IsDefault] DESC, x.[CreatedAt]) w
WHERE v.[WarehouseId] IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "Vehicle");

            migrationBuilder.DropColumn(
                name: "WarehouseName",
                table: "Vehicle");
        }
    }
}
