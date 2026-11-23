using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// نصوص البيانات المزروعة بعد توحيد المسمى «مستودع ← موقع» في الواجهة: وصف مرحلة تخصيص الشاصي، ومزايا الباقات.
    /// نصوص عرض فقط، منفصلة عن ترحيل الفروع.
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261123140000_LocationLabelsInSeeds")]
    public partial class LocationLabelsInSeeds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [CarStageDefinition] SET [DescAr] = N'ربط العقد المعتمد برقم شاسي VIN محدد من الموقع، وبدء فحص الجودة والتجهيز الفني PDI.' WHERE [Id] = N'allocation' AND [WorkflowType] = N'sales_contract';");

            migrationBuilder.Sql("UPDATE [PlanDefinition] SET [FeaturesAr] = N'عدد غير محدود من المستخدمين والمشرفين\nفروع ومواقع ومراكز تكلفة غير محدودة\nربط برمجي كامل (RESTful API Webhooks) مع المتاجر ونقاط البيع\nفواتير ضريبية برمز QR وفق المرحلة الأولى من ZATCA\nتعدد العملات وسعر الصرف التلقائي\nتقارير تحليلية ومؤشرات أداء مالية متقدمة وتصدير مخصص\nخادم وقاعدة بيانات مستقلة عالية الأداء ونسخ احتياطي فوري\nمدير حساب محاسبي معتمد مخصص ودعم على مدار الساعة 24/7' WHERE [Id] = N'enterprise';");

            migrationBuilder.Sql("UPDATE [PlanDefinition] SET [FeaturesAr] = N'حتى 10 مستخدمين مع إدارة أدوار متقدمة (RBAC)\nفواتير مبيعات ومشتريات غير محدودة شهرياً\nإدارة حتى 3 فروع ومواقع متعددة\nفواتير ضريبية (B2B) ومبسطة (B2C) برمز QR وفق المرحلة الأولى من ZATCA\nمحرك حساب متوسط التكلفة المرجح المتحرك (Moving Average Costing)\nشجرة حسابات احترافية مرنة والقيود اليومية الآلية\nتقارير مالية تفصيلية (قائمة الدخل، الميزانية العمومية، إقرار الضريبة)\nدعم فني سريع عبر الواتساب والهاتف' WHERE [Id] = N'professional';");

            migrationBuilder.Sql("UPDATE [PlanDefinition] SET [FeaturesAr] = N'مستخدمان وفرع وموقع واحد\nفواتير المبيعات والمشتريات ومرتجعاتها (حتى 500 فاتورة شهرياً)\nالعملاء والموردون والأصناف والبنوك وطرق الدفع\nدليل الحسابات والقيود وسندات القبض والصرف\nجرد المخزون وحركة الصنف\nالفوترة الإلكترونية (ZATCA)\nالتقارير الأساسية: ميزان المراجعة وقائمة الدخل والميزانية والإقرار الضريبي وكشف الحساب وأعمار الديون' WHERE [Id] = N'small_business';");

            migrationBuilder.Sql("UPDATE [PlanDefinition] SET [FeaturesAr] = N'مستخدمين 2 مع صلاحيات أساسية\nحتى 500 فاتورة مبيعات ومشتريات شهرياً\nفرع وموقع رئيسي واحد\nإصدار فواتير ضريبية مبسطة (B2C) مع QR كود TLV\nسندات القبض والصرف الأساسية\nشجرة حسابات مبسطة (4 مستويات)\nدعم فني عبر البريد وتحديثات دورية' WHERE [Id] = N'starter';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [CarStageDefinition] SET [DescAr] = N'ربط العقد المعتمد برقم شاسي VIN محدد من المستودع، وبدء فحص الجودة والتجهيز الفني PDI.' WHERE [Id] = N'allocation' AND [WorkflowType] = N'sales_contract';");

            migrationBuilder.Sql("UPDATE [PlanDefinition] SET [FeaturesAr] = N'عدد غير محدود من المستخدمين والمشرفين\nفروع ومستودعات ومراكز تكلفة غير محدودة\nربط برمجي كامل (RESTful API Webhooks) مع المتاجر ونقاط البيع\nفواتير ضريبية برمز QR وفق المرحلة الأولى من ZATCA\nتعدد العملات وسعر الصرف التلقائي\nتقارير تحليلية ومؤشرات أداء مالية متقدمة وتصدير مخصص\nخادم وقاعدة بيانات مستقلة عالية الأداء ونسخ احتياطي فوري\nمدير حساب محاسبي معتمد مخصص ودعم على مدار الساعة 24/7' WHERE [Id] = N'enterprise';");

            migrationBuilder.Sql("UPDATE [PlanDefinition] SET [FeaturesAr] = N'حتى 10 مستخدمين مع إدارة أدوار متقدمة (RBAC)\nفواتير مبيعات ومشتريات غير محدودة شهرياً\nإدارة حتى 3 فروع ومستودعات متعددة\nفواتير ضريبية (B2B) ومبسطة (B2C) برمز QR وفق المرحلة الأولى من ZATCA\nمحرك حساب متوسط التكلفة المرجح المتحرك (Moving Average Costing)\nشجرة حسابات احترافية مرنة والقيود اليومية الآلية\nتقارير مالية تفصيلية (قائمة الدخل، الميزانية العمومية، إقرار الضريبة)\nدعم فني سريع عبر الواتساب والهاتف' WHERE [Id] = N'professional';");

            migrationBuilder.Sql("UPDATE [PlanDefinition] SET [FeaturesAr] = N'مستخدمان وفرع ومستودع واحد\nفواتير المبيعات والمشتريات ومرتجعاتها (حتى 500 فاتورة شهرياً)\nالعملاء والموردون والأصناف والبنوك وطرق الدفع\nدليل الحسابات والقيود وسندات القبض والصرف\nجرد المخزون وحركة الصنف\nالفوترة الإلكترونية (ZATCA)\nالتقارير الأساسية: ميزان المراجعة وقائمة الدخل والميزانية والإقرار الضريبي وكشف الحساب وأعمار الديون' WHERE [Id] = N'small_business';");

            migrationBuilder.Sql("UPDATE [PlanDefinition] SET [FeaturesAr] = N'مستخدمين 2 مع صلاحيات أساسية\nحتى 500 فاتورة مبيعات ومشتريات شهرياً\nفرع ومستودع رئيسي واحد\nإصدار فواتير ضريبية مبسطة (B2C) مع QR كود TLV\nسندات القبض والصرف الأساسية\nشجرة حسابات مبسطة (4 مستويات)\nدعم فني عبر البريد وتحديثات دورية' WHERE [Id] = N'starter';");
        }
    }
}
