using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// الخياطة — تعديلات ما بعد التسليم: علامة على طلب التعديل لقطعة سُلّمت (مجاني، لا يغيّر حالة القطعة ولا الأمر). الطلبات القائمة تبقى عادية.
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261128170000_TailoringAfterSalesAlterations")]
    public partial class TailoringAfterSalesAlterations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AfterDelivery", table: "TailoringModification", type: "bit", nullable: false, defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AfterDelivery", table: "TailoringModification");
        }
    }
}
