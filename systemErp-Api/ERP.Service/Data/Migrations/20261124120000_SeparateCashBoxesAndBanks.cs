using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// فصل الصناديق عن البنوك في الدليل: حسابان رئيسيان مستقلان تحت الأصول المتداولة (11) بدل «النقدية والبنوك»:
    /// <list type="bullet">
    /// <item>111 يصير «الصناديق»: يبقى تحته الصندوق العام (1111) وصناديق المواقع.</item>
    /// <item>110 «البنوك» جديد: ينتقل تحته البنك الافتراضي (1112) وتحصيلات بوابة الدفع (1113) وحسابات البنوك
    /// (المرتبطة ببنك، أو رمز بنك في سجل البنوك، أو حساب طريقة دفع بنكية/بطاقة).</item>
    /// </list>
    /// النقل بالأب وحده: الرموز والقيود والأرصدة والمستويات (110 و111 في المستوى نفسه) كما هي. منشأة فيها حساب آخر بالرمز 110 لا يُمس شيء فيها.
    /// بيانات فقط، بلا تغيير في المخطط.
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261124120000_SeparateCashBoxesAndBanks")]
    public partial class SeparateCashBoxesAndBanks : Migration
    {
        /// <summary>مجموعة البنوك التي أنشأتها هذه الهجرة أو التأسيس (لا حساب للمستخدم بالرمز نفسه).</summary>
        private const string BanksGroup = "g.Code = '110' AND g.IsGroup = 1 AND g.IsSystem = 1 AND g.NameAr = N'البنوك'";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
INSERT INTO [Account] (Id, TenantId, Code, NameAr, NameEn, [Type], ParentCode, [Level], Balance, IsDebitNature, IsSystem, IsGroup, Currency,
                       CostCenterPolicy, IsActive, CreatedAt)
SELECT NEWID(), c.TenantId, '110', N'البنوك', N'Banks', c.[Type], c.ParentCode, c.[Level], 0, 1, 1, 1, c.Currency, c.CostCenterPolicy, 1, SYSUTCDATETIME()
FROM [Account] c
WHERE c.Code = '111' AND NOT EXISTS (SELECT 1 FROM [Account] x WHERE x.TenantId = c.TenantId AND x.Code = '110');");

            migrationBuilder.Sql($@"
UPDATE a SET a.ParentCode = '110', a.[Level] = g.[Level] + 1, a.UpdatedAt = SYSUTCDATETIME()
FROM [Account] a
JOIN [Account] g ON g.TenantId = a.TenantId AND {BanksGroup}
WHERE a.ParentCode = '111'
  AND (a.Code IN ('1112', '1113') OR a.LinkedEntityType = 'Bank'
       OR EXISTS (SELECT 1 FROM [BankEntity] b WHERE b.TenantId = a.TenantId AND b.AccountCode = a.Code)
       OR EXISTS (SELECT 1 FROM [PaymentMethodItem] p WHERE p.TenantId = a.TenantId AND p.LinkedAccountCode = a.Code AND p.[Type] IN ('bank', 'card')));");

            migrationBuilder.Sql($@"
UPDATE c SET c.NameAr = N'الصناديق', c.NameEn = N'Cash Boxes', c.UpdatedAt = SYSUTCDATETIME()
FROM [Account] c
JOIN [Account] g ON g.TenantId = c.TenantId AND {BanksGroup}
WHERE c.Code = '111' AND c.NameAr = N'النقدية والبنوك';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
UPDATE a SET a.ParentCode = '111', a.[Level] = g.[Level] + 1
FROM [Account] a
JOIN [Account] g ON g.TenantId = a.TenantId AND {BanksGroup}
WHERE a.ParentCode = '110';");
            migrationBuilder.Sql($@"
UPDATE c SET c.NameAr = N'النقدية والبنوك', c.NameEn = N'Cash and Banks'
FROM [Account] c
JOIN [Account] g ON g.TenantId = c.TenantId AND {BanksGroup}
WHERE c.Code = '111' AND c.NameAr = N'الصناديق';");
            migrationBuilder.Sql($@"
DELETE g FROM [Account] g
WHERE {BanksGroup}
  AND NOT EXISTS (SELECT 1 FROM [Account] x WHERE x.TenantId = g.TenantId AND x.ParentCode = '110')
  AND NOT EXISTS (SELECT 1 FROM [JournalEntryLine] l WHERE l.TenantId = g.TenantId AND l.AccountCode = '110');");
        }
    }
}
