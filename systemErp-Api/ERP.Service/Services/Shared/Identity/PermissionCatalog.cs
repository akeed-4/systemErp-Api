using A = ERP.Core.DTOs.Shared.ScreenAction;

namespace ERP.Service.Services.Shared;

/// <summary>مجموعة الإجراء في محرر الصلاحيات.</summary>
public enum PermissionActionGroup { Access, Data, Workflow, Reports }

public sealed record PermissionActionDefinition(A Action, string Code, PermissionActionGroup Group, string NameAr, string NameEn, A Legacy);

public sealed record PermissionModuleDefinition(string Id, string NameAr, string NameEn, string Icon);

/// <summary>
/// شاشة صلاحيات: الإجراءات التي تدعمها فعلاً، ومسارات الواجهة التي تفتحها (لحارس المسارات والقائمة).
/// <paramref name="InheritsFrom"/>: شاشة فُصلت من شاشة أقدم، فإن لم تُحفظ لها صلاحيات بعد ورثت صلاحيات الأصل.
/// <paramref name="Restricted"/>: صلاحية حسّاسة لا تُمنح افتراضياً إلا للأدوار الإدارية.
/// <paramref name="Legacy"/>: مصدر الإجراء في الأعمدة الخمسة القديمة إن خالف الافتراضي في <see cref="PermissionCatalog.Actions"/>.
/// </summary>
public sealed record PermissionScreenDefinition(
    string Id, string ModuleId, string NameAr, string NameEn, A[] Actions, string[] Routes,
    bool Restricted = false, string? InheritsFrom = null, IReadOnlyDictionary<A, A>? Legacy = null);

/// <summary>
/// كتالوج الصلاحيات: المصدر الوحيد للوحدات والشاشات والإجراءات. يقرأ منه السرفر (التحقق) والواجهة (المحرر، الحارس، الأزرار)
/// عبر GET /permissions/catalog، فلا تُكرَّر قائمة الشاشات في الواجهة. إضافة إجراء لشاشة = سطر هنا + سمة على الـ endpoint.
/// </summary>
public static class PermissionCatalog
{
    public static readonly PermissionActionDefinition[] Actions =
    {
        new(A.View, "view", PermissionActionGroup.Access, "عرض", "View", A.View),
        new(A.Create, "create", PermissionActionGroup.Data, "إضافة", "Create", A.Create),
        new(A.Edit, "edit", PermissionActionGroup.Data, "تعديل", "Edit", A.Edit),
        new(A.Delete, "delete", PermissionActionGroup.Data, "حذف", "Delete", A.Delete),
        new(A.Review, "review", PermissionActionGroup.Workflow, "مراجعة", "Review", A.Approve),
        new(A.Approve, "approve", PermissionActionGroup.Workflow, "اعتماد", "Approve", A.Approve),
        new(A.Reject, "reject", PermissionActionGroup.Workflow, "رفض", "Reject", A.Approve),
        new(A.Cancel, "cancel", PermissionActionGroup.Workflow, "إلغاء", "Cancel", A.Approve),
        new(A.Sign, "sign", PermissionActionGroup.Workflow, "توقيع", "Sign", A.Approve),
        new(A.Close, "close", PermissionActionGroup.Workflow, "إغلاق", "Close", A.Approve),
        new(A.Reopen, "reopen", PermissionActionGroup.Workflow, "إعادة فتح", "Reopen", A.Approve),
        new(A.Post, "post", PermissionActionGroup.Workflow, "ترحيل", "Post", A.Edit),
        new(A.Import, "import", PermissionActionGroup.Workflow, "استيراد", "Import", A.Create),
        new(A.Discount, "discount", PermissionActionGroup.Workflow, "خصم يدوي", "Manual Discount", A.Approve),
        // أدوار دورة العقد: مُعد العقد، المراجع القانوني، المراجع المالي، لجنة الفحص، أمين المستودع (صاحب الصلاحية = اعتماد، المحاسب = ترحيل)
        new(A.Submit, "submit", PermissionActionGroup.Workflow, "إرسال للمراجعة", "Submit for Review", A.Edit),
        new(A.LegalReview, "legal_review", PermissionActionGroup.Workflow, "مراجعة قانونية", "Legal Review", A.Approve),
        new(A.FinancialReview, "financial_review", PermissionActionGroup.Workflow, "مراجعة مالية", "Financial Review", A.Approve),
        new(A.Inspect, "inspect", PermissionActionGroup.Workflow, "فحص واستلام أولي", "Inspection & Acceptance", A.Approve),
        new(A.Receive, "receive", PermissionActionGroup.Workflow, "استلام ومرتجع", "Receive & Return", A.Create),
        // التكاملات الخارجية (واتساب): الربط والفصل إداريان، والإرسال للمستخدم الذي ينشئ المستندات
        new(A.Connect, "connect", PermissionActionGroup.Workflow, "ربط الحساب", "Connect Account", A.Edit),
        new(A.Send, "send", PermissionActionGroup.Workflow, "إرسال", "Send", A.Create),
        new(A.Disconnect, "disconnect", PermissionActionGroup.Workflow, "فصل الحساب", "Disconnect Account", A.Delete),
        new(A.Print, "print", PermissionActionGroup.Reports, "طباعة", "Print", A.View),
        new(A.Export, "export", PermissionActionGroup.Reports, "تصدير Excel", "Export Excel", A.View),
        new(A.Pdf, "pdf", PermissionActionGroup.Reports, "تصدير PDF", "Export PDF", A.View),
    };

    public static readonly PermissionModuleDefinition[] Modules =
    {
        new("contracts", "العقود", "Contracts", "description"),
        new("sales", "المبيعات", "Sales", "point_of_sale"),
        new("purchases", "المشتريات", "Purchases", "shopping_cart"),
        new("accounting", "المحاسبة", "Accounting", "account_balance"),
        new("inventory", "المخزون", "Inventory", "inventory_2"),
        new("master-data", "البيانات الرئيسية", "Master Data", "folder_shared"),
        new("hr", "الموارد البشرية", "Human Resources", "groups"),
        new("car-showroom", "معارض السيارات", "Car Showroom", "directions_car"),
        new("reports", "التقارير", "Reports", "insights"),
        new("settings", "الإعدادات", "Settings", "settings"),
    };

    private static readonly A[] Docs = { A.Print, A.Export, A.Pdf };
    private static A[] Crud(params A[] extra) => new[] { A.View, A.Create, A.Edit, A.Delete }.Concat(extra).Concat(Docs).ToArray();
    private static Dictionary<A, A> CancelFromEdit => new() { [A.Cancel] = A.Edit };

    public static readonly PermissionScreenDefinition[] Screens =
    {
        new(CommercialContractsScreenId, "contracts", "العقود التجارية ومراحلها", "Commercial Contracts & Stages",
            Crud(A.Submit, A.LegalReview, A.FinancialReview, A.Approve, A.Inspect, A.Receive, A.Post), new[] { "contracts" }, InheritsFrom: "sales"),

        new("sales", "sales", "فواتير المبيعات ونقاط البيع (POS)", "Sales Invoices & POS",
            Crud(A.Approve, A.Post, A.Cancel, A.Discount),
            new[] { "sales", "pos", "agreements", "sales/agreements", "purchases/agreements", "orders", "delivery-notes", "sales/delivery-notes", "purchases/delivery-notes", "contracts/delivery-notes" },
            Legacy: CancelFromEdit),
        new("sales-returns", "sales", "مرتجع المبيعات والإشعارات الدائنة", "Sales Returns & Credit Notes",
            Crud(A.Approve, A.Post), new[] { "sales/returns" }),
        new(PermissionService.InvoiceTaxTreatmentOverrideScreenId, "sales", "الفواتير: تجاوز التصنيف الضريبي للصنف في سطر الفاتورة", "Invoices: Override Item Tax Treatment on a Line",
            new[] { A.Edit }, Array.Empty<string>(), Restricted: true),

        new("purchases", "purchases", "فواتير المشتريات وتكلفة المخزون", "Purchase Invoices",
            Crud(A.Approve, A.Post), new[] { "purchases", "stock-transfers", "costing" }),

        new("accounts", "accounting", "دليل الحسابات والقيود اليومية", "Chart of Accounts",
            Crud(A.Approve, A.Post, A.Cancel, A.Close), new[] { "accounts-tree", "cost-centers", "bank-reconciliation", "fiscal-periods", "fixed-assets" }),
        // بُعد محاسبي على القيود: يرث صلاحية دليل الحسابات (حيث تُدار المراكز) حتى تُحفظ له صلاحية مستقلة
        new(PermissionService.InvoiceCostCenterScreenId, "accounting", "الفواتير: تعيين وتعديل مركز التكلفة", "Invoices: Assign & Change Cost Center",
            new[] { A.Edit }, Array.Empty<string>(), InheritsFrom: "accounts"),
        new("accounting-settings", "accounting", "إعدادات الحسابات المحاسبية وطريقة الجرد", "Accounting Settings & Inventory Method",
            new[] { A.View, A.Edit }, new[] { "accounting-settings" }, InheritsFrom: "accounts"),
        new("vouchers", "accounting", "سندات القبض والصرف", "Receipt & Payment Vouchers",
            Crud(A.Approve, A.Post, A.Cancel), new[] { "vouchers" }),

        new("warehouses", "inventory", "المستودعات", "Warehouses", Crud(), new[] { "master-data/warehouses" }),
        new("inventory-counts", "inventory", "جرد المخزون والمركبات واعتماده", "Inventory Counts & Approvals",
            Crud(A.Approve, A.Reject, A.Cancel), new[] { "inventory-counts", "showroom/inventory-counts" }, Legacy: CancelFromEdit),

        new("master-data", "master-data", "البيانات الرئيسية (العملاء/الموردين/الأصناف)", "Master Data & Entities",
            Crud(A.Import), new[] { "master-data" }),
        new(PermissionService.ItemStandardCostScreenId, "master-data", "الأصناف: تعديل التكلفة المعيارية لصنف مستخدم (إعادة تقييم)", "Items: Change Standard Cost of a Used Item (Revaluation)",
            new[] { A.Edit }, Array.Empty<string>(), Restricted: true),

        new("hr", "hr", "شؤون الموظفين والرواتب", "Human Resources & Payroll",
            Crud(A.Approve, A.Reject, A.Cancel, A.Post, A.Import), new[] { "hr", "payroll" }, Legacy: CancelFromEdit),

        new("car-showroom", "car-showroom", "إدارة وتعاريف معارض السيارات والمبايعات", "Car Showroom & Automotive",
            Crud(A.Approve, A.Reject, A.Cancel, A.Import),
            new[] { "showroom", "car-showroom", "car-procurement", "car-contracts", "master-data/car-agents", "master-data/car-colors" },
            Legacy: CancelFromEdit),
        new(PermissionService.CarCostViewScreenId, "car-showroom", "السيارات: رؤية التكلفة والربح", "Cars: View Cost & Profit",
            new[] { A.View }, Array.Empty<string>(), Restricted: true),
        new(PermissionService.ChargeTreatmentOverrideScreenId, "car-showroom", "السيارات: تعديل معالجة سطر المصروف", "Cars: Override Charge Treatment",
            new[] { A.Edit }, Array.Empty<string>(), Restricted: true),
        new(PermissionService.LandedCostApprovalScreenId, "car-showroom", "السيارات: اعتماد سند التكاليف الإضافية", "Cars: Approve Landed Cost Vouchers",
            new[] { A.Approve }, Array.Empty<string>(), Restricted: true),
        new(PermissionService.SoldVehicleAdjustmentScreenId, "car-showroom", "السيارات: قيود التعديل على سيارات مباعة", "Cars: Cost Adjustments on Sold Vehicles",
            new[] { A.Approve }, Array.Empty<string>(), Restricted: true),

        new("dashboard", "reports", "لوحة المؤشرات والملخص المالي", "Dashboard & Summary", new[] { A.View }, new[] { "dashboard" }),
        new("reports", "reports", "التقارير المالية والإقرار الضريبي", "Financial Reports",
            new[] { A.View }.Concat(Docs).ToArray(), new[] { "reports", "financial-statements" }),

        new("approval-policies", "settings", "سياسات الموافقات والطلبات", "Approval Policies & Requests",
            new[] { A.View, A.Create, A.Edit, A.Delete, A.Approve, A.Reject }, Array.Empty<string>()),
        new("zatca", "settings", "الربط الإلكتروني والفوترة (ZATCA)", "ZATCA Integration", new[] { A.View, A.Edit }, new[] { "zatca-integration" }),
        // تعديل = إدارة (المزامنة والقوالب)
        new(WhatsAppScreenId, "settings", "تكامل واتساب للأعمال", "WhatsApp Business Integration",
            new[] { A.View, A.Connect, A.Edit, A.Disconnect }, new[] { "whatsapp-integration" }, Restricted: true),
        new(WhatsAppSendScreenId, "sales", "إرسال المستندات عبر واتساب", "Send Documents via WhatsApp",
            new[] { A.View, A.Send }, Array.Empty<string>()),
        new("user-permissions", "settings", "إدارة صلاحيات المستخدمين والشاشات", "User Roles & Screen Permissions",
            new[] { A.View, A.Create, A.Edit, A.Delete }, new[] { "users", "permissions", "payment-gateway" }),
    };

    public const string CommercialContractsScreenId = "commercial-contracts";
    public const string WhatsAppScreenId = "whatsapp";
    public const string WhatsAppSendScreenId = "whatsapp-send";

    private static readonly Dictionary<string, PermissionScreenDefinition> ById = Screens.ToDictionary(s => s.Id);
    private static readonly Dictionary<A, PermissionActionDefinition> ActionsByEnum = Actions.ToDictionary(a => a.Action);
    private static readonly Dictionary<string, A> ActionsByCode = Actions.ToDictionary(a => a.Code, a => a.Action, StringComparer.OrdinalIgnoreCase);

    public static PermissionScreenDefinition? Find(string screenId) => ById.GetValueOrDefault(screenId);
    public static string Code(A action) => ActionsByEnum[action].Code;
    public static PermissionActionDefinition Action(A action) => ActionsByEnum[action];
    public static bool TryParse(string? code, out A action)
    {
        action = default;
        return code != null && ActionsByCode.TryGetValue(code.Trim(), out action);
    }

    /// <summary>عمود الأعمدة الخمسة الذي يُشتق منه الإجراء لسجل قديم.</summary>
    public static A LegacySource(string screenId, A action)
        => Find(screenId)?.Legacy?.TryGetValue(action, out var src) == true ? src : ActionsByEnum[action].Legacy;

    /// <summary>عدد كل الصلاحيات (شاشة × إجراء) في الكتالوج.</summary>
    public static int TotalPermissions => Screens.Sum(s => s.Actions.Length);
}
