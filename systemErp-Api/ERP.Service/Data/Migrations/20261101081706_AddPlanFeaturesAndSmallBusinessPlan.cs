using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanFeaturesAndSmallBusinessPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FeatureKeys",
                table: "Subscription",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "commercial_cycle,landed_costs,cost_centers,fixed_assets,multi_currency,bank_reconciliation,approval_policies,stock_transfers");

            migrationBuilder.AddColumn<int>(
                name: "MaxInvoicesPerMonth",
                table: "Subscription",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FeatureKeys",
                table: "PlanDefinition",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "commercial_cycle,landed_costs,cost_centers,fixed_assets,multi_currency,bank_reconciliation,approval_policies,stock_transfers");

            // الاشتراكات القائمة: كل الميزات (القيمة الافتراضية أعلاه) وحد فواتير باقتها للتنبيه فقط
            migrationBuilder.Sql("UPDATE s SET s.MaxInvoicesPerMonth = p.MaxInvoicesPerMonth FROM [Subscription] AS s INNER JOIN [PlanDefinition] AS p ON p.Id = s.PlanType;");

            migrationBuilder.UpdateData(
                table: "PlanDefinition",
                keyColumn: "Id",
                keyValue: "enterprise",
                column: "FeatureKeys",
                value: "commercial_cycle,landed_costs,cost_centers,fixed_assets,multi_currency,bank_reconciliation,approval_policies,stock_transfers");

            migrationBuilder.UpdateData(
                table: "PlanDefinition",
                keyColumn: "Id",
                keyValue: "professional",
                column: "FeatureKeys",
                value: "commercial_cycle,landed_costs,cost_centers,fixed_assets,multi_currency,bank_reconciliation,approval_policies,stock_transfers");

            migrationBuilder.UpdateData(
                table: "PlanDefinition",
                keyColumn: "Id",
                keyValue: "starter",
                column: "FeatureKeys",
                value: "commercial_cycle,landed_costs,cost_centers,fixed_assets,multi_currency,bank_reconciliation,approval_policies,stock_transfers");

            migrationBuilder.InsertData(
                table: "PlanDefinition",
                columns: new[] { "Id", "BadgeAr", "BadgeEn", "Branches", "DescriptionAr", "DescriptionEn", "FeatureKeys", "FeaturesAr", "FeaturesEn", "IsActive", "IsPopular", "MaxInvoicesPerMonth", "MaxUsers", "ModuleKeys", "NameAr", "NameEn", "PriceMonthly", "PriceYearly", "SortOrder", "UpdatedAt", "ZatcaPhase2Enabled" },
                values: new object[] { "small_business", "للمنشآت الصغيرة", "For Small Businesses", 1, "كل ما تحتاجه المنشأة الصغيرة: المبيعات والمشتريات والمخزون والمحاسبة والتقارير الأساسية، بلا تعقيد.", "Everything a small business needs: sales, purchases, inventory, accounting and core reports, without the complexity.", "", "مستخدمان وفرع ومستودع واحد\nفواتير المبيعات والمشتريات ومرتجعاتها (حتى 500 فاتورة شهرياً)\nالعملاء والموردون والأصناف والبنوك وطرق الدفع\nدليل الحسابات والقيود وسندات القبض والصرف\nجرد المخزون وحركة الصنف\nالفوترة الإلكترونية (ZATCA)\nالتقارير الأساسية: ميزان المراجعة وقائمة الدخل والميزانية والإقرار الضريبي وكشف الحساب وأعمار الديون", "2 users, 1 branch & warehouse\nSales & purchase invoices and returns (up to 500/month)\nCustomers, suppliers, items, banks & payment methods\nChart of accounts, journals, receipt & payment vouchers\nStock counts & item movement\nE-invoicing (ZATCA)\nCore reports: trial balance, income statement, balance sheet, VAT return, account statement, aging", true, false, 500, 2, "accounting", "باقة المنشآت الصغيرة", "Small Business Plan", 149m, 1490m, 0, null, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "PlanDefinition",
                keyColumn: "Id",
                keyValue: "small_business");

            migrationBuilder.DropColumn(
                name: "FeatureKeys",
                table: "Subscription");

            migrationBuilder.DropColumn(
                name: "MaxInvoicesPerMonth",
                table: "Subscription");

            migrationBuilder.DropColumn(
                name: "FeatureKeys",
                table: "PlanDefinition");
        }
    }
}
