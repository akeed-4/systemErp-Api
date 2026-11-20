using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// طريقة تسعير الفاتورة (شامل/غير شامل الضريبة): عمود على جداول الفواتير الخمسة، والقائمة كلها «غير شامل» فلا تتغير حساباتها.
    /// ومنح صلاحية التسعير غير الشامل لكل صلاحيات محفوظة (دور أو مستخدم) تُصدر فواتير مبيعات أو مشتريات اليوم، فلا يفقد أحد
    /// قدرته الحالية (الأدوار المخصّصة لا افتراضي لها). التسعير الشامل جديد: لا يُمنح هنا (الأدوار الإدارية تملكه افتراضياً).
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261120090000_AddInvoicePricingMethod")]
    public partial class AddInvoicePricingMethod : Migration
    {
        private static readonly string[] Tables = { "Invoice", "SalesInvoice", "PurchaseInvoice", "SalesReturn", "PurchaseReturn" };

        private const string ExclusiveScreen = "invoice-vat-exclusive-pricing";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
            {
                migrationBuilder.AddColumn<string>(
                    name: "PricingMethod",
                    table: table,
                    type: "nvarchar(max)",
                    nullable: false,
                    defaultValue: "Exclusive");
            }

            // من يملك إنشاء أو تعديل على المبيعات أو المشتريات (بالإجراءات المستقلة أو بالأعمدة القديمة) يملك التسعير غير الشامل
            migrationBuilder.Sql($@"
INSERT INTO ScreenPermissionItem (Id, TenantId, CreatedAt, UserRolePermissionId, ScreenId, CanView, CanCreate, CanEdit, CanDelete, CanApprove, Actions)
SELECT NEWID(), p.TenantId, SYSUTCDATETIME(), p.Id, '{ExclusiveScreen}', 0, 0, 1, 0, 0, 'edit'
FROM UserRolePermission p
WHERE EXISTS (
    SELECT 1 FROM ScreenPermissionItem s
    WHERE s.UserRolePermissionId = p.Id AND s.ScreenId IN ('sales', 'purchases')
      AND ((s.Actions IS NULL AND (s.CanCreate = 1 OR s.CanEdit = 1))
        OR (s.Actions IS NOT NULL AND (',' + REPLACE(s.Actions, ' ', '') + ',' LIKE '%,create,%'
                                    OR ',' + REPLACE(s.Actions, ' ', '') + ',' LIKE '%,edit,%'))))
  AND NOT EXISTS (
    SELECT 1 FROM ScreenPermissionItem x WHERE x.UserRolePermissionId = p.Id AND x.ScreenId = '{ExclusiveScreen}');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DELETE FROM ScreenPermissionItem WHERE ScreenId IN ('{ExclusiveScreen}', 'invoice-vat-inclusive-pricing');");
            foreach (var table in Tables)
            {
                migrationBuilder.DropColumn(name: "PricingMethod", table: table);
            }
        }
    }
}
