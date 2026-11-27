using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// فصل معارض السيارات عن الباقات العامة: البداية والمتقدمة والمؤسسات تصبح «المحاسبة ونقاط البيع» فلا تُزرع للمنشأة الجديدة
    /// حسابات المعارض (412/413/116...) ما لم تختر باقتها، وتُضاف «باقة معارض السيارات» بالوحدة نفسها.
    /// الباقات تُعدَّل من الكتالوج فقط: اشتراكات المنشآت القائمة (ModuleKeys المنسوخة) لا تتغير، والتجديد ينقل المعارض لمن كانت لديه.
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261127180000_SeparateCarShowroomPlan")]
    public partial class SeparateCarShowroomPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // حذف car_showroom من القائمة أياً كانت بقية وحداتها (قد يضيف مدير المنصة whatsapp وغيرها)
            migrationBuilder.Sql(@"
UPDATE [PlanDefinition]
SET [ModuleKeys] = SUBSTRING(REPLACE(N',' + [ModuleKeys] + N',', N',car_showroom,', N','), 2,
                             LEN(REPLACE(N',' + [ModuleKeys] + N',', N',car_showroom,', N',')) - 2)
WHERE [Id] IN (N'starter', N'professional', N'enterprise') AND N',' + [ModuleKeys] + N',' LIKE N'%,car_showroom,%'
  AND [ModuleKeys] <> N'car_showroom';

IF NOT EXISTS (SELECT 1 FROM [PlanDefinition] WHERE [Id] = N'car_showroom')
INSERT INTO [PlanDefinition] ([Id], [NameAr], [NameEn], [DescriptionAr], [DescriptionEn], [BadgeAr], [BadgeEn], [IsPopular],
    [FeaturesAr], [FeaturesEn], [PriceMonthly], [PriceYearly], [MaxUsers], [MaxInvoicesPerMonth], [Branches], [ZatcaPhase2Enabled],
    [ModuleKeys], [FeatureKeys], [IsActive], [SortOrder], [UpdatedAt])
VALUES (N'car_showroom', N'باقة معارض السيارات', N'Car Showroom Plan',
    N'مخصصة لمعارض السيارات: مخزون المركبات وشراؤها واستيرادها وعقود بيعها، مع المحاسبة ونقاط البيع.',
    N'Built for car showrooms: vehicle inventory, procurement, import and sales contracts, with accounting and POS.',
    N'لمعارض السيارات', N'For Car Showrooms', 0,
    N'حتى 10 مستخدمين مع إدارة أدوار متقدمة' + NCHAR(10) + N'فواتير غير محدودة شهرياً' + NCHAR(10) + N'حتى 3 فروع ومواقع متعددة' + NCHAR(10)
        + N'مخزون المركبات بالهيكل واللون والموديل' + NCHAR(10) + N'أوامر شراء السيارات وتكاليف الاستيراد' + NCHAR(10)
        + N'عقود بيع السيارات والعربون والإضافات' + NCHAR(10) + N'تقارير المعرض وربحية كل مركبة' + NCHAR(10) + N'نقاط البيع والفوترة الإلكترونية (ZATCA)',
    N'Up to 10 users with advanced roles' + NCHAR(10) + N'Unlimited invoices per month' + NCHAR(10) + N'Up to 3 branches & multi-warehouse' + NCHAR(10)
        + N'Vehicle inventory by VIN, color and model' + NCHAR(10) + N'Car procurement orders and landed import costs' + NCHAR(10)
        + N'Car sales contracts, deposits and add-ons' + NCHAR(10) + N'Showroom reports and per-vehicle profitability' + NCHAR(10) + N'POS and e-invoicing (ZATCA)',
    499, 4990, 10, NULL, 3, 0,
    N'accounting,car_showroom,pos',
    N'commercial_cycle,landed_costs,cost_centers,fixed_assets,multi_currency,bank_reconciliation,approval_policies,stock_transfers',
    1, 4, NULL);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DELETE FROM [PlanDefinition] WHERE [Id] = N'car_showroom';
UPDATE [PlanDefinition] SET [ModuleKeys] = REPLACE(N',' + [ModuleKeys], N',accounting', N',accounting,car_showroom')
WHERE [Id] IN (N'starter', N'professional', N'enterprise') AND N',' + [ModuleKeys] + N',' NOT LIKE N'%,car_showroom,%'
  AND N',' + [ModuleKeys] + N',' LIKE N'%,accounting,%';
UPDATE [PlanDefinition] SET [ModuleKeys] = SUBSTRING([ModuleKeys], 2, LEN([ModuleKeys]))
WHERE [Id] IN (N'starter', N'professional', N'enterprise') AND LEFT([ModuleKeys], 1) = N',';");
        }
    }
}
