using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>التصنيف الضريبي على تعريف الصنف. الأصناف القائمة تبقى «خاضعة» (سلوكها السابق بنسبة الصنف) بلا تغيير.</summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261009180000_AddProductVatCategory")]
    public partial class AddProductVatCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "VatCategory", table: "Product", type: "nvarchar(max)", nullable: false, defaultValue: "Standard");
            migrationBuilder.AddColumn<string>(name: "VatExemptionReasonCode", table: "Product", type: "nvarchar(max)", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "VatCategory", table: "Product");
            migrationBuilder.DropColumn(name: "VatExemptionReasonCode", table: "Product");
        }
    }
}
