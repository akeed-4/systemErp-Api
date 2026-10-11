using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodLockConcurrencyAndVatCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BooksLockedThrough",
                table: "Tenant",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Product",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<string>(
                name: "VatCategory",
                table: "InvoiceItem",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "Standard");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Account",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            // السطور القائمة بنسبة 0 وبلا ضريبة ثابتة كانت تُعامل صفرية؛ ما عداها أساسي (القيمة الافتراضية)
            migrationBuilder.Sql("UPDATE InvoiceItem SET VatCategory = 'ZeroRated' WHERE VatRate = 0 AND ISNULL(VatAmountOverride, 0) = 0;");

            migrationBuilder.UpdateData(
                table: "PlanDefinition",
                keyColumn: "Id",
                keyValue: "Enterprise",
                columns: new[] { "FeaturesAr", "FeaturesEn", "ZatcaPhase2Enabled" },
                values: new object[] { "عدد غير محدود من المستخدمين والمشرفين\nفروع ومستودعات ومراكز تكلفة غير محدودة\nربط برمجي كامل (RESTful API Webhooks) مع المتاجر ونقاط البيع\nفواتير ضريبية برمز QR وفق المرحلة الأولى من ZATCA\nتعدد العملات وسعر الصرف التلقائي\nتقارير تحليلية ومؤشرات أداء مالية متقدمة وتصدير مخصص\nخادم وقاعدة بيانات مستقلة عالية الأداء ونسخ احتياطي فوري\nمدير حساب محاسبي معتمد مخصص ودعم على مدار الساعة 24/7", "Unlimited users and branch supervisors\nUnlimited branches, warehouses & cost centers\nFull RESTful API & POS webhooks integration\nQR tax invoices compliant with ZATCA Phase 1\nMulti-currency support with auto FX rates\nAdvanced financial BI dashboards & custom export\nDedicated high-performance tenant database & real-time backup\nDedicated account manager & 24/7 priority SLA", false });

            migrationBuilder.UpdateData(
                table: "PlanDefinition",
                keyColumn: "Id",
                keyValue: "Professional",
                columns: new[] { "DescriptionAr", "DescriptionEn", "FeaturesAr", "FeaturesEn", "ZatcaPhase2Enabled" },
                values: new object[] { "الخيار الأكثر طلباً للشركات والمؤسسات المتوسطة مع فواتير ضريبية برمز QR (المرحلة الأولى من ZATCA) ومحاسبة التكاليف.", "Most popular choice for growing businesses with QR tax invoices (ZATCA Phase 1) and a costing engine.", "حتى 10 مستخدمين مع إدارة أدوار متقدمة (RBAC)\nفواتير مبيعات ومشتريات غير محدودة شهرياً\nإدارة حتى 3 فروع ومستودعات متعددة\nفواتير ضريبية (B2B) ومبسطة (B2C) برمز QR وفق المرحلة الأولى من ZATCA\nمحرك حساب متوسط التكلفة المرجح المتحرك (Moving Average Costing)\nشجرة حسابات احترافية مرنة والقيود اليومية الآلية\nتقارير مالية تفصيلية (قائمة الدخل، الميزانية العمومية، إقرار الضريبة)\nدعم فني سريع عبر الواتساب والهاتف", "Up to 10 users with advanced RBAC permissions\nUnlimited invoices per month\nUp to 3 branches & multi-warehouse inventory\nTax (B2B) and simplified (B2C) invoices with QR code (ZATCA Phase 1)\nWeighted Moving Average costing engine\nEnterprise chart of accounts with auto journal entries\nFinancial reports (P&L, Balance Sheet, VAT Return)\nPriority phone & WhatsApp support", false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BooksLockedThrough",
                table: "Tenant");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "VatCategory",
                table: "InvoiceItem");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Account");

            migrationBuilder.UpdateData(
                table: "PlanDefinition",
                keyColumn: "Id",
                keyValue: "Enterprise",
                columns: new[] { "FeaturesAr", "FeaturesEn", "ZatcaPhase2Enabled" },
                values: new object[] { "عدد غير محدود من المستخدمين والمشرفين\nفروع ومستودعات ومراكز تكلفة غير محدودة\nربط برمجي كامل (RESTful API Webhooks) مع المتاجر ونقاط البيع\nربط تلقائي بالكامل مع بوابة Fatoora ZATCA وتخزين سحابي للـ CSID\nتعدد العملات وسعر الصرف التلقائي\nتقارير تحليلية ومؤشرات أداء مالية متقدمة وتصدير مخصص\nخادم وقاعدة بيانات مستقلة عالية الأداء ونسخ احتياطي فوري\nمدير حساب محاسبي معتمد مخصص ودعم على مدار الساعة 24/7", "Unlimited users and branch supervisors\nUnlimited branches, warehouses & cost centers\nFull RESTful API & POS webhooks integration\nAutomated Fatoora ZATCA portal with cloud CSID storage\nMulti-currency support with auto FX rates\nAdvanced financial BI dashboards & custom export\nDedicated high-performance tenant database & real-time backup\nDedicated account manager & 24/7 priority SLA", true });

            migrationBuilder.UpdateData(
                table: "PlanDefinition",
                keyColumn: "Id",
                keyValue: "Professional",
                columns: new[] { "DescriptionAr", "DescriptionEn", "FeaturesAr", "FeaturesEn", "ZatcaPhase2Enabled" },
                values: new object[] { "الخيار الأكثر طلباً للشركات والمؤسسات المتوسطة مع دعم كامل للربط والتكامل ZATCA ومحاسبة التكاليف.", "Most popular choice for growing businesses with full ZATCA Phase 2 clearance and costing engine.", "حتى 10 مستخدمين مع إدارة أدوار متقدمة (RBAC)\nفواتير مبيعات ومشتريات غير محدودة شهرياً\nإدارة حتى 3 فروع ومستودعات متعددة\nربط وتكامل مع هيئة الزكاة ZATCA Phase 2 (فواتير ضريبية B2B واعتماد لحظي)\nمحرك حساب متوسط التكلفة المرجح المتحرك (Moving Average Costing)\nشجرة حسابات احترافية مرنة والقيود اليومية الآلية\nتقارير مالية تفصيلية (قائمة الدخل، الميزانية العمومية، إقرار الضريبة)\nدعم فني سريع عبر الواتساب والهاتف", "Up to 10 users with advanced RBAC permissions\nUnlimited invoices per month\nUp to 3 branches & multi-warehouse inventory\nZATCA Phase 2 Integration (B2B Clearance & B2C Reporting)\nWeighted Moving Average costing engine\nEnterprise chart of accounts with auto journal entries\nFinancial reports (P&L, Balance Sheet, VAT Return)\nPriority phone & WhatsApp support", true });
        }
    }
}
