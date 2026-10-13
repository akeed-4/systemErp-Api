using ERP.Service.Data;

namespace ERP.Service.Services.CarShowroom;

/// <summary>
/// أنواع المصاريف الأولية لكل منشأة. مكانها الوحيد هنا: تُزرع عند تهيئة المنشأة الجديدة، وتُستكمل للمنشآت القائمة
/// عند أول استخدام (<see cref="EnsureAsync"/>) دون تكرار.
/// المعالجة والفئة الضريبية وقابلية الاسترداد قيم ابتدائية قابلة للتعديل من شاشة أنواع المصاريف؛
/// الإعدادات الضريبية يحددها محاسب المنشأة وليست قاعدة ثابتة في النظام.
/// </summary>
public static class ExpenseTypeSeed
{
    /// <summary>نوعا الرصيد الافتتاحي: تُسجَّل عليهما أرصدة حقول التكلفة السابقة لسجل المصاريف.</summary>
    public const string OpeningAdditional = "OPENING_ADDITIONAL";
    public const string OpeningPreparation = "OPENING_PREPARATION";

    private const ChargeTreatment Cap = ChargeTreatment.Capitalize;
    private const ChargeTreatment Exp = ChargeTreatment.Expense;
    private const ChargeTreatment Rec = ChargeTreatment.RechargeToCustomer;
    private const ChargeAllocationMethod Eq = ChargeAllocationMethod.Equal;
    private const ChargeAllocationMethod Val = ChargeAllocationMethod.ByValue;
    private const VatCategory Std = VatCategory.Standard;
    private const VatCategory Oos = VatCategory.OutOfScope;

    private sealed record Seed(string Code, string Ar, string En, ExpenseStage Stage, ChargeTreatment Treatment, ChargeAllocationMethod Method,
        VatCategory Vat, bool ShowToCustomer = false, bool IsInputTax = false, bool IsDisbursement = false);

    private static readonly Seed[] Seeds =
    {
        // شراء / استيراد
        new("FREIGHT", "شحن", "Freight", ExpenseStage.Purchase, Cap, Eq, Std),
        new("FREIGHT_INSURANCE", "تأمين شحن", "Freight Insurance", ExpenseStage.Purchase, Cap, Val, Std),
        new("CUSTOMS_DUTY", "رسوم جمركية", "Customs Duty", ExpenseStage.Purchase, Cap, Val, Oos),
        // ضريبة الاستيراد: المبلغ كله ضريبة مدخلات قابلة للاسترداد فلا يدخل التكلفة (قابل للضبط من النوع)
        new("IMPORT_VAT", "ضريبة استيراد", "Import VAT", ExpenseStage.Purchase, Cap, Val, Std, IsInputTax: true),
        new("CUSTOMS_CLEARANCE", "مخلّص جمركي", "Customs Clearance Agent", ExpenseStage.Purchase, Cap, Eq, Std),
        new("PORT_STORAGE", "أرضيات وتخزين ميناء", "Port Storage and Demurrage", ExpenseStage.Purchase, Cap, Eq, Std),
        new("INLAND_TRANSPORT", "نقل داخلي", "Inland Transport", ExpenseStage.Purchase, Cap, Eq, Std),
        new("SABER", "شهادة مطابقة (سابر)", "Conformity Certificate (SABER)", ExpenseStage.Purchase, Cap, Eq, Std),
        new("PURCHASE_BROKER", "عمولة وسيط شراء", "Purchase Broker Commission", ExpenseStage.Purchase, Cap, Val, Std),
        // تم
        new("TAMM_TRANSFER_IN", "نقل ملكية للمعرض", "Ownership Transfer to Showroom", ExpenseStage.Purchase, Cap, Eq, Oos),
        new("TAMM_HOLD", "تحفظ", "Vehicle Hold", ExpenseStage.Holding, Exp, Eq, Oos),
        new("TAMM_AUTO_HOLD", "تحفظ تلقائي", "Automatic Vehicle Hold", ExpenseStage.Holding, Exp, Eq, Oos),
        new("TAMM_VIOLATIONS", "استعلام مخالفات", "Traffic Violations Inquiry", ExpenseStage.Holding, Exp, Eq, Oos),
        new("TAMM_REG_ISSUE", "إصدار استمارة", "Registration Issuance", ExpenseStage.Holding, Cap, Eq, Oos),
        new("TAMM_REG_RENEW", "تجديد استمارة", "Registration Renewal", ExpenseStage.Holding, Cap, Eq, Oos),
        new("TAMM_AUTH_INTERNAL", "تفويض داخلي", "Internal Driving Authorization", ExpenseStage.Holding, Exp, Eq, Oos),
        new("TAMM_AUTH_EXTERNAL", "تفويض خارجي", "External Driving Authorization", ExpenseStage.Holding, Exp, Eq, Oos),
        new("TAMM_ACTUAL_DRIVER", "قائد فعلي", "Actual Driver", ExpenseStage.Holding, Exp, Eq, Oos),
        new("TAMM_TRANSFER_OUT", "نقل ملكية للمشتري", "Ownership Transfer to Buyer", ExpenseStage.Sale, Rec, Eq, Oos, ShowToCustomer: true, IsDisbursement: true),
        // تجهيز
        new("MAINTENANCE", "صيانة", "Maintenance", ExpenseStage.Holding, Cap, Eq, Std),
        new("REPAIR", "إصلاح", "Repair", ExpenseStage.Holding, Cap, Eq, Std),
        new("POLISHING", "تلميع", "Polishing and Detailing", ExpenseStage.Holding, Cap, Eq, Std),
        new("PERIODIC_INSPECTION", "فحص دوري", "Periodic Inspection", ExpenseStage.Holding, Cap, Eq, Std),
        // بيع
        new("PLATES", "لوحات", "Plates", ExpenseStage.Sale, Rec, Eq, Oos, ShowToCustomer: true, IsDisbursement: true),
        new("CUSTOMER_ADDONS", "إضافات للعميل (تظليل، حماية...)", "Customer Add-ons (Tint, Protection...)", ExpenseStage.Sale, Rec, Eq, Std, ShowToCustomer: true),
        new("SALES_COMMISSION", "عمولة بائع", "Salesperson Commission", ExpenseStage.Sale, Exp, Eq, Oos),
        new("VEHICLE_INSURANCE", "تأمين السيارة", "Vehicle Insurance", ExpenseStage.Sale, Exp, Eq, Std),
        // عام
        new("TAMM_SUBSCRIPTION", "اشتراك تم السنوي", "Tamm Annual Subscription", ExpenseStage.General, Exp, Eq, Std),
        // رصيد افتتاحي
        new(OpeningAdditional, "رصيد افتتاحي - تكاليف إضافية", "Opening Balance - Additional Costs", ExpenseStage.Purchase, Cap, Eq, Oos),
        new(OpeningPreparation, "رصيد افتتاحي - تجهيز", "Opening Balance - Preparation", ExpenseStage.Holding, Cap, Eq, Oos),
    };

    public static IEnumerable<ExpenseType> Build() => Seeds.Select(s => new ExpenseType
    {
        Code = s.Code, NameAr = s.Ar, NameEn = s.En, Stage = s.Stage, DefaultTreatment = s.Treatment, DefaultAllocationMethod = s.Method,
        VatCategory = s.Vat, IsVatRecoverable = true, IsInputTax = s.IsInputTax, IsDisbursement = s.IsDisbursement, ShowToCustomer = s.ShowToCustomer, IsSystem = true, IsActive = true,
    });

    /// <summary>يضيف للمنشأة الحالية الأنواع المزروعة الناقصة فقط (المنشآت القديمة هُيّئت قبل هذه الأنواع).</summary>
    public static async Task EnsureAsync(ErpDbContext db, CancellationToken ct)
    {
        var existing = await db.Set<ExpenseType>().Select(t => t.Code).ToListAsync(ct);
        var missing = Build().Where(t => !existing.Contains(t.Code)).ToList();
        if (missing.Count == 0) return;
        db.AddRange(missing);
        await db.SaveChangesAsync(ct);
    }
}
