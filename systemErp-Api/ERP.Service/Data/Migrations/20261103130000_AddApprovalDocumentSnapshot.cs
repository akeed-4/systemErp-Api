using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ERP.Service.Data;

#nullable disable

namespace ERP.Service.Data.Migrations;

[DbContext(typeof(ErpDbContext))]
[Migration("20261103130000_AddApprovalDocumentSnapshot")]
public partial class AddApprovalDocumentSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(
            name: "DocumentDataSnapshot",
            table: "ApprovalRequest",
            type: "nvarchar(max)",
            nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "DocumentDataSnapshot", table: "ApprovalRequest");
}
