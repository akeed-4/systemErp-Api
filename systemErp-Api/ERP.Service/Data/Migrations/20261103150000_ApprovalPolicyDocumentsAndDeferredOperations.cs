using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ERP.Service.Data;

#nullable disable

namespace ERP.Service.Data.Migrations;

/// <summary>
/// السياسة الواحدة تغطي عدة مستندات (DocumentType ← DocumentTypes مفصولة بفاصلة، والقيم الحالية مستند واحد صالح كما هي)،
/// وطلب الاعتماد يحمل العملية المحجوزة التي تُنفَّذ عند اعتماده النهائي.
/// </summary>
[DbContext(typeof(ErpDbContext))]
[Migration("20261103150000_ApprovalPolicyDocumentsAndDeferredOperations")]
public partial class ApprovalPolicyDocumentsAndDeferredOperations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(name: "DocumentType", table: "ApprovalPolicy", newName: "DocumentTypes");

        migrationBuilder.AddColumn<bool>(name: "IsDeferred", table: "ApprovalRequest", type: "bit", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<string>(name: "Handler", table: "ApprovalRequest", type: "nvarchar(max)", nullable: true);
        migrationBuilder.AddColumn<string>(name: "Payload", table: "ApprovalRequest", type: "nvarchar(max)", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Payload", table: "ApprovalRequest");
        migrationBuilder.DropColumn(name: "Handler", table: "ApprovalRequest");
        migrationBuilder.DropColumn(name: "IsDeferred", table: "ApprovalRequest");

        // سياسة تغطي عدة مستندات تعود لأولها
        migrationBuilder.Sql("UPDATE ApprovalPolicy SET DocumentTypes = LEFT(DocumentTypes, CHARINDEX(',', DocumentTypes + ',') - 1)");
        migrationBuilder.RenameColumn(name: "DocumentTypes", table: "ApprovalPolicy", newName: "DocumentType");
    }
}
