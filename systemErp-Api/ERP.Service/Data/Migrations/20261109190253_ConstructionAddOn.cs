using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConstructionAddOn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "PlatformModuleDefinition",
                columns: new[] { "Key", "DescriptionAr", "DescriptionEn", "Icon", "IsActive", "NameAr", "NameEn", "Number", "SortOrder" },
                values: new object[] { "construction", "المناقصات وعقود جدول الكميات والمشاريع والتقارير اليومية والمواد والمستخلصات. سعرها حسب الاتفاق.", "Tenders, BOQ contracts, projects, daily site reports, materials and certificates. Priced by agreement.", "engineering", true, "المقاولات", "Construction", 6, 6 });

            // المقاولات صارت إضافة مرخّصة: تُمنح تلقائياً لمن يستخدمها فعلاً (عقد بجدول كميات أو بنود في مكتبة المقاولات) فلا يتوقف
            // عمله، والبقية يمنحها لهم مدير المنصة بعد الاتفاق. تعمل على كل قاعدة (المشتركة والمستقلة) لأن الاشتراك مع بيانات منشأته.
            migrationBuilder.Sql("""
                UPDATE s SET s.ModuleKeys = s.ModuleKeys + ',construction'
                FROM [Subscription] s
                WHERE (',' + s.ModuleKeys + ',') LIKE '%,accounting,%'
                  AND (',' + s.ModuleKeys + ',') NOT LIKE '%,construction,%'
                  AND (EXISTS (SELECT 1 FROM [CommercialContract] c
                               JOIN [ContractTypeDefinition] t ON t.[Code] = c.[ContractType] AND t.[TenantId] = c.[TenantId]
                               WHERE c.[TenantId] = s.[TenantId] AND t.[Billing] = 'boq_certificate')
                    OR EXISTS (SELECT 1 FROM [WorkItem] w WHERE w.[TenantId] = s.[TenantId]));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [Subscription] SET ModuleKeys = REPLACE(ModuleKeys, ',construction', '') WHERE ModuleKeys LIKE '%,construction%';");

            migrationBuilder.DeleteData(
                table: "PlatformModuleDefinition",
                keyColumn: "Key",
                keyValue: "construction");
        }
    }
}
