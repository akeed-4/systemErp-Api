namespace ERP.Core.Models.Shared;

/// <summary>عملية على مستند تخضع لسياسة اعتماد (create | edit | delete | post | approve | cancel ...).</summary>
public sealed record ApprovalActionDefinition(string Key, string NameAr, string NameEn);

/// <summary>مستند يمكن ربط سياسات الاعتماد به، ووحدته في النظام، والعمليات التي يفرض السرفر اعتمادها عليه.</summary>
public sealed record ApprovalDocumentDefinition(string Key, string Module, string NameAr, string NameEn, IReadOnlyList<string> Actions);

/// <summary>وحدة (موديول) تُجمَّع تحتها المستندات في شاشة السياسات.</summary>
public sealed record ApprovalModuleDefinition(string Key, string NameAr, string NameEn, string? PlatformModule);

/// <summary>
/// فهرس المستندات الخاضعة لسياسات الاعتماد: المصدر الوحيد لما تعرضه شاشة السياسات ولما يقبله التحقق من السياسة.
/// كل مستند هنا مربوط فعلياً في السرفر: الفواتير والمرتجعات تبقى مسودة حتى اعتمادها، والجرد يمر بمسار اعتماده،
/// وبقية المستندات تُحجز عمليتها (إنشاء/تعديل/حذف/ترحيل...) بطلب اعتماد ولا تُنفَّذ إلا عند الاعتماد النهائي.
/// </summary>
public static class ApprovalDocumentCatalog
{
    public const string Create = "create";
    public const string Edit = "edit";
    public const string Delete = "delete";
    public const string Post = "post";
    public const string Approve = "approve";
    public const string Cancel = "cancel";
    public const string Reverse = "reverse";
    public const string Complete = "complete";
    public const string Return = "return";
    public const string Quick = "quick";

    public static readonly IReadOnlyList<ApprovalActionDefinition> ActionDefinitions = new ApprovalActionDefinition[]
    {
        new(Create, "إنشاء / حفظ", "Create / save"),
        new(Edit, "تعديل", "Edit"),
        new(Delete, "حذف", "Delete"),
        new(Post, "ترحيل", "Post"),
        new(Approve, "اعتماد", "Approve"),
        new(Cancel, "إلغاء", "Cancel"),
        new(Reverse, "عكس", "Reverse"),
        new(Complete, "إتمام", "Complete"),
        new(Return, "إرجاع", "Return"),
        new(Quick, "عملية سريعة", "Quick operation"),
    };

    public static readonly IReadOnlyList<ApprovalModuleDefinition> Modules = new ApprovalModuleDefinition[]
    {
        new("sales", "المبيعات", "Sales", null),
        new("purchases", "المشتريات", "Purchases", null),
        new("accounting", "الحسابات والخزينة", "Accounting & treasury", null),
        new("inventory", "المخزون", "Inventory", null),
        new("contracts", "العقود", "Contracts", null),
        new("hr", "الموارد البشرية والرواتب", "HR & payroll", "hr"),
        new("car_showroom", "معارض السيارات", "Car showroom", "car_showroom"),
    };

    public static readonly IReadOnlyList<ApprovalDocumentDefinition> Documents = new ApprovalDocumentDefinition[]
    {
        // المبيعات
        new("sales", "sales", "فاتورة مبيعات", "Sales invoice", new[] { Create }),
        new("sales_return", "sales", "مرتجع مبيعات", "Sales return", new[] { Create }),
        new("quotation", "sales", "عرض سعر", "Quotation", new[] { Create, Edit, Delete }),
        new("sales_order", "sales", "أمر بيع", "Sales order", new[] { Create, Edit, Delete }),
        new("delivery_note", "sales", "سند تسليم", "Delivery note", new[] { Create, Edit, Delete }),
        new("delivery_return", "sales", "مرتجع سند تسليم", "Delivery return", new[] { Create, Delete }),

        // المشتريات
        new("purchase", "purchases", "فاتورة مشتريات", "Purchase invoice", new[] { Create }),
        new("purchase_return", "purchases", "مرتجع مشتريات", "Purchase return", new[] { Create }),
        new("purchase_order", "purchases", "أمر شراء", "Purchase order", new[] { Create, Edit, Delete }),
        new("material_requisition", "purchases", "طلب شراء داخلي", "Purchase requisition", new[] { Create, Edit, Delete }),
        new("purchase_landed_cost", "purchases", "تكاليف الشراء الإضافية", "Purchase landed cost", new[] { Create, Edit, Approve, Cancel, Delete }),

        // الحسابات والخزينة
        new("receipt_voucher", "accounting", "سند قبض", "Receipt voucher", new[] { Create, Edit, Delete }),
        new("payment_voucher", "accounting", "سند صرف", "Payment voucher", new[] { Create, Edit, Delete }),
        new("journal_entry", "accounting", "قيد يومية يدوي", "Manual journal entry", new[] { Create, Edit, Delete, Reverse }),

        // المخزون
        new("inventory_count", "inventory", "اعتماد جرد", "Inventory count approval", new[] { Approve }),
        new("stock_transfer", "inventory", "تحويل مخزني", "Stock transfer", new[] { Create }),

        // العقود
        new("commercial_contract", "contracts", "عقد تجاري", "Commercial contract", new[] { Create, Edit, Delete }),
        new("project_material_plan", "contracts", "احتياجات مواد المشروع", "Project material requirements", new[] { Approve }),
        new("contract_variation", "contracts", "أمر تغيير", "Variation order", new[] { Approve }),

        // الموارد البشرية
        new("payroll", "hr", "مسير رواتب", "Payroll run", new[] { Approve, Post }),
        new("employee_loan", "hr", "سلفة موظف", "Employee loan", new[] { Create, Edit, Cancel }),
        new("end_of_service", "hr", "مكافأة نهاية خدمة", "End of service", new[] { Post }),

        // معارض السيارات
        new("car_sales_contract", "car_showroom", "عقد بيع سيارة", "Car sales contract", new[] { Create, Edit, Quick, Complete, Cancel, Return, Delete }),
        new("car_procurement_order", "car_showroom", "أمر شراء سيارات", "Car procurement order", new[] { Create, Edit, Quick, Delete }),
        new("landed_cost_voucher", "car_showroom", "سند تكاليف استيراد", "Landed cost voucher", new[] { Create, Edit, Approve, Cancel, Delete }),
        new("import_cost_advance", "car_showroom", "دفعة مقدمة لتكاليف الاستيراد", "Import cost advance", new[] { Create, Edit, Approve, Cancel, Delete }),
        new("prepaid_expense", "car_showroom", "مصروف مدفوع مقدماً", "Prepaid expense", new[] { Create, Cancel }),
    };

    private static readonly Dictionary<string, ApprovalDocumentDefinition> ByKey = Documents.ToDictionary(d => d.Key, StringComparer.Ordinal);

    public static ApprovalDocumentDefinition? Find(string? key) => key != null && ByKey.TryGetValue(key, out var d) ? d : null;

    public static bool Supports(string documentType, string action) => Find(documentType)?.Actions.Contains(action) == true;

    /// <summary>أنواع مستندات السياسة كما تُخزَّن (مفصولة بفاصلة) إلى قائمة.</summary>
    public static List<string> Split(string? stored)
        => string.IsNullOrWhiteSpace(stored) ? new() : stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();

    public static string Join(IEnumerable<string> types) => string.Join(',', types.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct());
}
