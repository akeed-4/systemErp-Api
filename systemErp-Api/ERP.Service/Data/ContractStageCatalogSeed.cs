using ERP.Core.Models.Shared;
using S = ERP.Core.Models.Shared.ContractStageDefinition;

namespace ERP.Service.Data;

/// <summary>
/// حالات العقد في ثلاثة أطوار: قبل التوقيع (مسودة، مراجعة، اعتماد)، التنفيذ (ساري)، الإقفال (استلام أولي، مغلق)؛
/// وموقوف وملغي خارج المسار. الاستلامات والمرتجعات والفواتير مستندات داخل «ساري» لا مراحل.
/// AllowedActions: ما يُسمح به من إجراءات المستندات والعمليات والعقد في الحالة؛ الانتقال بين الحالات من جدول الانتقالات وحده.
/// </summary>
public static class ContractStageCatalogSeed
{
    public static readonly ContractStageDefinition[] All =
    [
        new() { Id = S.Draft, Number = 1, Phase = "pre_signing", SlaDays = 7, ShortNameAr = "مسودة", ShortNameEn = "Draft",
            NameAr = "مسودة وصياغة العقد", NameEn = "Contract Drafting", DescAr = "صياغة البنود والكميات والأسعار والشروط", DescEn = "Drafting items, quantities, prices and terms",
            Icon = "edit_document", BadgeColor = "bg-slate-700 text-slate-200 border-slate-600", HeaderColor = "from-slate-700 to-slate-800",
            AllowedActions = "edit,delete,submit" },
        new() { Id = S.Review, Number = 2, Phase = "pre_signing", SlaDays = 5, ShortNameAr = "مراجعة", ShortNameEn = "Review",
            NameAr = "المراجعة القانونية والمالية", NameEn = "Legal and Financial Review", DescAr = "موافقة المراجع القانوني والمراجع المالي معاً", DescEn = "Approval by both the legal and the financial reviewer",
            Icon = "gavel", BadgeColor = "bg-amber-950 text-amber-300 border-amber-800", HeaderColor = "from-amber-700 to-amber-800",
            AllowedActions = "legal_review,financial_review" },
        new() { Id = S.Approval, Number = 3, Phase = "pre_signing", SlaDays = 3, ShortNameAr = "اعتماد", ShortNameEn = "Approval",
            NameAr = "الاعتماد والتوقيع", NameEn = "Approval and Signing", DescAr = "اعتماد صاحب الصلاحية بعد النسخة الموقعة والضمان البنكي", DescEn = "Authorized approval after the signed copy and bank guarantee",
            Icon = "draw", BadgeColor = "bg-blue-950 text-blue-300 border-blue-800", HeaderColor = "from-blue-700 to-blue-800",
            AllowedActions = "approve" },
        new() { Id = S.Active, Number = 4, Phase = "execution", SlaDays = null, ShortNameAr = "ساري", ShortNameEn = "Active",
            NameAr = "ساري وقيد التنفيذ", NameEn = "Active Execution", DescAr = "الاستلامات والمرتجعات والفواتير المرحلية", DescEn = "Receipts, returns and progress invoices",
            Icon = "engineering", BadgeColor = "bg-emerald-950 text-emerald-300 border-emerald-800", HeaderColor = "from-emerald-700 to-emerald-800",
            AllowedActions = "receive,post,inspect,approve" },
        new() { Id = S.InitialAcceptance, Number = 5, Phase = "closing", SlaDays = 14, ShortNameAr = "استلام", ShortNameEn = "Acceptance",
            NameAr = "الاستلام الأولي", NameEn = "Initial Acceptance", DescAr = "فحص المخرجات واعتماد محاضر الاستلام والفوترة النهائية", DescEn = "Inspecting deliverables, acceptance records and final billing",
            Icon = "checklist", BadgeColor = "bg-cyan-950 text-cyan-300 border-cyan-800", HeaderColor = "from-cyan-700 to-cyan-800",
            AllowedActions = "receive,post,approve" },
        new() { Id = S.Closed, Number = 6, Phase = "closing", SlaDays = null, ShortNameAr = "مغلق", ShortNameEn = "Closed",
            NameAr = "الإغلاق النهائي", NameEn = "Final Closure", DescAr = "الإفراج عن المحتجز والتسوية الختامية", DescEn = "Retention release and final settlement",
            Icon = "verified", BadgeColor = "bg-teal-950 text-teal-300 border-teal-800", HeaderColor = "from-teal-700 to-teal-800",
            AllowedActions = "post" },
        new() { Id = S.Suspended, Number = 7, Phase = "side", SlaDays = null, ShortNameAr = "موقوف", ShortNameEn = "Suspended",
            NameAr = "موقوف مؤقتاً", NameEn = "Suspended", DescAr = "تنفيذ العقد موقوف بقرار مسبب", DescEn = "Execution suspended by a reasoned decision",
            Icon = "pause_circle", BadgeColor = "bg-orange-950 text-orange-300 border-orange-800", HeaderColor = "from-orange-700 to-orange-800",
            AllowedActions = "approve" },
        new() { Id = S.Cancelled, Number = 8, Phase = "side", SlaDays = null, ShortNameAr = "ملغي", ShortNameEn = "Cancelled",
            NameAr = "ملغي قبل التفعيل", NameEn = "Cancelled", DescAr = "أُلغي قبل أن يصبح سارياً", DescEn = "Cancelled before becoming active",
            Icon = "cancel", BadgeColor = "bg-rose-950 text-rose-300 border-rose-800", HeaderColor = "from-rose-700 to-rose-800",
            AllowedActions = "" },
    ];
}

/// <summary>جدول الانتقالات المسموحة (يُزرع مرة ويُعدَّل من قاعدة البيانات). الإجراءات من كتالوج الصلاحيات على شاشة العقود.</summary>
public static class ContractTransitionSeed
{
    public static readonly ContractTransition[] All =
    [
        new() { Id = 1, FromStatus = S.Draft, ToStatus = S.Review, NameAr = "إرسال للمراجعة", NameEn = "Submit for review", RequiredActions = "submit", Condition = "complete_data", SortOrder = 1 },
        new() { Id = 2, FromStatus = S.Review, ToStatus = S.Approval, NameAr = "موافقة المراجعة", NameEn = "Approve review", RequiredActions = "legal_review,financial_review", Condition = "both_reviews", SortOrder = 1 },
        new() { Id = 3, FromStatus = S.Review, ToStatus = S.Draft, NameAr = "إعادة للمسودة", NameEn = "Return to draft", RequiredActions = "legal_review,financial_review", RequiresNote = true, Tone = "neutral", SortOrder = 2 },
        new() { Id = 4, FromStatus = S.Approval, ToStatus = S.Active, NameAr = "اعتماد وتفعيل", NameEn = "Approve & activate", RequiredActions = "approve", Condition = "signed_guarantee_limit", SortOrder = 1 },
        new() { Id = 5, FromStatus = S.Approval, ToStatus = S.Draft, NameAr = "رفض الاعتماد", NameEn = "Reject approval", RequiredActions = "approve", RequiresNote = true, Tone = "neutral", SortOrder = 2 },
        new() { Id = 6, FromStatus = S.Active, ToStatus = S.InitialAcceptance, NameAr = "استلام أولي", NameEn = "Initial acceptance", RequiredActions = "inspect", Condition = "all_received", SortOrder = 1 },
        new() { Id = 7, FromStatus = S.Active, ToStatus = S.Suspended, NameAr = "إيقاف", NameEn = "Suspend", RequiredActions = "approve", RequiresNote = true, Tone = "danger", SortOrder = 2 },
        new() { Id = 8, FromStatus = S.Suspended, ToStatus = S.Active, NameAr = "استئناف", NameEn = "Resume", RequiredActions = "approve", RequiresNote = true, SortOrder = 1 },
        new() { Id = 9, FromStatus = S.InitialAcceptance, ToStatus = S.Closed, NameAr = "إغلاق نهائي", NameEn = "Close", RequiredActions = "approve", Condition = "all_billed_retention_released", SortOrder = 1 },
        new() { Id = 10, FromStatus = S.Draft, ToStatus = S.Cancelled, NameAr = "إلغاء", NameEn = "Cancel", RequiredActions = "approve", RequiresNote = true, Tone = "danger", SortOrder = 9 },
        new() { Id = 11, FromStatus = S.Review, ToStatus = S.Cancelled, NameAr = "إلغاء", NameEn = "Cancel", RequiredActions = "approve", RequiresNote = true, Tone = "danger", SortOrder = 9 },
        new() { Id = 12, FromStatus = S.Approval, ToStatus = S.Cancelled, NameAr = "إلغاء", NameEn = "Cancel", RequiredActions = "approve", RequiresNote = true, Tone = "danger", SortOrder = 9 },
    ];
}

/// <summary>أنواع العقود الافتراضية لكل منشأة جديدة (وللقائمة عبر الترحيل).</summary>
public static class ContractTypeSeed
{
    public static IEnumerable<ContractTypeDefinition> Build() =>
    [
        new() { Code = "supply_materials", NameAr = "توريد مواد", NameEn = "Materials Supply", Workflow = ContractWorkflows.Full, Billing = ContractBilling.AfterReceipt, RequiresGuarantee = true, SortOrder = 1 },
        new() { Code = "monthly_maintenance", NameAr = "صيانة شهرية", NameEn = "Monthly Maintenance", Workflow = ContractWorkflows.Simple, Billing = ContractBilling.Direct, SortOrder = 2 },
        new() { Code = "construction_project", NameAr = "مشروع إنشائي", NameEn = "Construction Project", Workflow = ContractWorkflows.Full, Billing = ContractBilling.AfterReceipt, RequiresGuarantee = true, SortOrder = 3 },
        new() { Code = "existing_contract", NameAr = "تسجيل عقد قائم", NameEn = "Register Existing Contract", Workflow = ContractWorkflows.Simple, Billing = ContractBilling.Direct, SortOrder = 4 },
    ];
}
