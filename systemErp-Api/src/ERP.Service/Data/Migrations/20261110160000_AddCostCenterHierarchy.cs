using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations;

[DbContext(typeof(ErpDbContext))]
[Migration("20261110160000_AddCostCenterHierarchy")]
public partial class AddCostCenterHierarchy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ParentCostCenterId",
            table: "CostCenter",
            type: "uniqueidentifier",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ParentCostCenterId",
            table: "CostCenter");
    }
}
