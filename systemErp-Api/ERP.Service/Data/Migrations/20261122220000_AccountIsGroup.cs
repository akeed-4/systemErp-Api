using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// نوع الحساب رئيسي/فرعي (IsGroup): الرئيسي تجميعي يقبل الأبناء ولا يُرحَّل عليه. الحسابات القائمة: رئيسي من له أبناء،
    /// ومجموعات الكيانات في الشجرة الافتراضية (111 النقدية والبنوك، 112 العملاء، 114 المخزون، 211 الموردون) ولو كانت فارغة.
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261122220000_AccountIsGroup")]
    public partial class AccountIsGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(name: "IsGroup", table: "Account", type: "bit", nullable: false, defaultValue: false);

            migrationBuilder.Sql(@"
UPDATE a SET a.IsGroup = 1
FROM [Account] a
WHERE EXISTS (SELECT 1 FROM [Account] c WHERE c.TenantId = a.TenantId AND c.ParentCode = a.Code)
   OR (a.IsSystem = 1 AND a.Code IN (N'111', N'112', N'114', N'211'));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "IsGroup", table: "Account");
        }
    }
}
