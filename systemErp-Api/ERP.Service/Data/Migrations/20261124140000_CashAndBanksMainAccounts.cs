using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// البنوك (110) والصندوق/النقدية (111) حسابان رئيسيان تحت الأصول المتداولة (11)، و1111 «الصندوق الرئيسي» تحت 111.
    /// للقواعد التي طُبقت عليها <c>SeparateCashBoxesAndBanks</c> بأسماء سابقة: تسمية 111 «الصندوق/النقدية» وتثبيت المجموعتين رئيسيتين،
    /// وتسمية 1111 «الصندوق الرئيسي» إن بقي باسمه الافتراضي. أسماء وعلَم «رئيسي» فقط: لا رموز ولا آباء ولا قيود ولا أرصدة.
    /// منشأة فيها حساب للمستخدم بالرمز 110 (ليس حساب نظام) لا يُمس شيء فيها. التشغيل المتكرر لا يغيّر شيئاً.
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261124140000_CashAndBanksMainAccounts")]
    public partial class CashAndBanksMainAccounts : Migration
    {
        /// <summary>منشأة فُصلت بنوكها: مجموعة البنوك حساب نظام.</summary>
        private const string Separated = "EXISTS (SELECT 1 FROM [Account] g WHERE g.TenantId = c.TenantId AND g.Code = '110' AND g.IsSystem = 1)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
UPDATE c SET c.NameAr = N'الصندوق/النقدية', c.NameEn = N'Cash Boxes', c.IsGroup = 1, c.UpdatedAt = SYSUTCDATETIME()
FROM [Account] c
WHERE c.Code = '111' AND {Separated}
  AND (c.NameAr IN (N'النقدية والبنوك', N'الصناديق', N'النقدية/الصندوق') OR (c.NameAr = N'الصندوق/النقدية' AND c.IsGroup = 0));");

            migrationBuilder.Sql(@"
UPDATE g SET g.IsGroup = 1, g.UpdatedAt = SYSUTCDATETIME()
FROM [Account] g
WHERE g.Code = '110' AND g.IsSystem = 1 AND g.IsGroup = 0;");

            migrationBuilder.Sql($@"
UPDATE c SET c.NameAr = N'الصندوق الرئيسي', c.NameEn = N'Main Cash Box', c.UpdatedAt = SYSUTCDATETIME()
FROM [Account] c
WHERE c.Code = '1111' AND c.IsSystem = 1 AND c.NameAr = N'الصندوق' AND c.ParentCode = '111' AND {Separated};");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE c SET c.NameAr = N'الصندوق', c.NameEn = N'Cash on Hand'
FROM [Account] c
WHERE c.Code = '1111' AND c.IsSystem = 1 AND c.NameAr = N'الصندوق الرئيسي';");
        }
    }
}
