using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanMarketingDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BadgeAr",
                table: "PlanDefinition",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BadgeEn",
                table: "PlanDefinition",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DescriptionAr",
                table: "PlanDefinition",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DescriptionEn",
                table: "PlanDefinition",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FeaturesAr",
                table: "PlanDefinition",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FeaturesEn",
                table: "PlanDefinition",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsPopular",
                table: "PlanDefinition",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "PlanDefinition",
                keyColumn: "Id",
                keyValue: "Enterprise",
                columns: new[] { "BadgeAr", "BadgeEn", "DescriptionAr", "DescriptionEn", "FeaturesAr", "FeaturesEn", "IsPopular" },
                values: new object[] { "للمجموعات الكبرى", "Corporate & Holding", "حل متكامل ومخصص للمجموعات التجارية والشركات الكبرى وفروعها مع بنية تحتية مخصصة وربط API مفتوح.", "Custom corporate solution for large holdings and chains with dedicated infrastructure and API webhooks.", "عدد غير محدود من المستخدمين والمشرفين\nفروع ومستودعات ومراكز تكلفة غير محدودة\nربط برمجي كامل (RESTful API Webhooks) مع المتاجر ونقاط البيع\nربط تلقائي بالكامل مع بوابة Fatoora ZATCA وتخزين سحابي للـ CSID\nتعدد العملات وسعر الصرف التلقائي\nتقارير تحليلية ومؤشرات أداء مالية متقدمة وتصدير مخصص\nخادم وقاعدة بيانات مستقلة عالية الأداء ونسخ احتياطي فوري\nمدير حساب محاسبي معتمد مخصص ودعم على مدار الساعة 24/7", "Unlimited users and branch supervisors\nUnlimited branches, warehouses & cost centers\nFull RESTful API & POS webhooks integration\nAutomated Fatoora ZATCA portal with cloud CSID storage\nMulti-currency support with auto FX rates\nAdvanced financial BI dashboards & custom export\nDedicated high-performance tenant database & real-time backup\nDedicated account manager & 24/7 priority SLA", false });

            migrationBuilder.UpdateData(
                table: "PlanDefinition",
                keyColumn: "Id",
                keyValue: "Professional",
                columns: new[] { "BadgeAr", "BadgeEn", "DescriptionAr", "DescriptionEn", "FeaturesAr", "FeaturesEn", "IsPopular" },
                values: new object[] { "الأكثر طلباً ⭐", "Most Popular ⭐", "الخيار الأكثر طلباً للشركات والمؤسسات المتوسطة مع دعم كامل للربط والتكامل ZATCA ومحاسبة التكاليف.", "Most popular choice for growing businesses with full ZATCA Phase 2 clearance and costing engine.", "حتى 10 مستخدمين مع إدارة أدوار متقدمة (RBAC)\nفواتير مبيعات ومشتريات غير محدودة شهرياً\nإدارة حتى 3 فروع ومستودعات متعددة\nربط وتكامل مع هيئة الزكاة ZATCA Phase 2 (فواتير ضريبية B2B واعتماد لحظي)\nمحرك حساب متوسط التكلفة المرجح المتحرك (Moving Average Costing)\nشجرة حسابات احترافية مرنة والقيود اليومية الآلية\nتقارير مالية تفصيلية (قائمة الدخل، الميزانية العمومية، إقرار الضريبة)\nدعم فني سريع عبر الواتساب والهاتف", "Up to 10 users with advanced RBAC permissions\nUnlimited invoices per month\nUp to 3 branches & multi-warehouse inventory\nZATCA Phase 2 Integration (B2B Clearance & B2C Reporting)\nWeighted Moving Average costing engine\nEnterprise chart of accounts with auto journal entries\nFinancial reports (P&L, Balance Sheet, VAT Return)\nPriority phone & WhatsApp support", true });

            migrationBuilder.UpdateData(
                table: "PlanDefinition",
                keyColumn: "Id",
                keyValue: "Starter",
                columns: new[] { "BadgeAr", "BadgeEn", "DescriptionAr", "DescriptionEn", "FeaturesAr", "FeaturesEn", "IsPopular" },
                values: new object[] { "للمنشآت الناشئة", "For Startups", "مثالية للمنشآت والمتاجر الناشئة ورواد الأعمال لتلبية متطلبات الفوترة الضريبية وإدارة المعاملات الأساسية.", "Ideal for startups, sole proprietors, and small shops to meet basic invoicing and tax compliance.", "مستخدمين 2 مع صلاحيات أساسية\nحتى 500 فاتورة مبيعات ومشتريات شهرياً\nفرع ومستودع رئيسي واحد\nإصدار فواتير ضريبية مبسطة (B2C) مع QR كود TLV\nسندات القبض والصرف الأساسية\nشجرة حسابات مبسطة (4 مستويات)\nدعم فني عبر البريد وتحديثات دورية", "Up to 2 users with basic roles\nUp to 500 invoices/month\n1 branch & single warehouse\nSimplified Tax Invoices (B2C) with TLV QR Code\nBasic receipt & payment vouchers\nStandard chart of accounts (4 levels)\nEmail support & regular updates", false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BadgeAr",
                table: "PlanDefinition");

            migrationBuilder.DropColumn(
                name: "BadgeEn",
                table: "PlanDefinition");

            migrationBuilder.DropColumn(
                name: "DescriptionAr",
                table: "PlanDefinition");

            migrationBuilder.DropColumn(
                name: "DescriptionEn",
                table: "PlanDefinition");

            migrationBuilder.DropColumn(
                name: "FeaturesAr",
                table: "PlanDefinition");

            migrationBuilder.DropColumn(
                name: "FeaturesEn",
                table: "PlanDefinition");

            migrationBuilder.DropColumn(
                name: "IsPopular",
                table: "PlanDefinition");
        }
    }
}
