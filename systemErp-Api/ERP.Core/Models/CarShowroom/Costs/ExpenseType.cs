namespace ERP.Core.Models.CarShowroom;

/// <summary>
/// نوع مصروف/تكلفة إضافية على السيارات (شحن، جمارك، رسوم تم، تجهيز...). قابل للإدارة لكل منشأة،
/// ومنه يرث سطر المصروف معالجته وطريقة توزيعه وفئته الضريبية.
/// الإعدادات الضريبية هنا (الفئة، قابلية الاسترداد، ضريبة المدخلات) يحددها محاسب المنشأة؛ القيم الأولية نقطة بداية فقط.
/// </summary>
public class ExpenseType : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public ExpenseStage Stage { get; set; } = ExpenseStage.Purchase;
    public ChargeTreatment DefaultTreatment { get; set; } = ChargeTreatment.Capitalize;
    public ChargeAllocationMethod DefaultAllocationMethod { get; set; } = ChargeAllocationMethod.Equal;
    public VatCategory VatCategory { get; set; } = VatCategory.Standard;
    /// <summary>ضريبة السطر قابلة للاسترداد كضريبة مدخلات؛ غير المستردة تدخل في مبلغ المصروف نفسه.</summary>
    public bool IsVatRecoverable { get; set; } = true;
    /// <summary>مبلغ السطر كله ضريبة (مثل ضريبة الاستيراد): إن كانت قابلة للاسترداد لا تدخل تكلفة السيارة.</summary>
    public bool IsInputTax { get; set; }
    /// <summary>سبب الإعفاء برمز هيئة الزكاة (VATEX-SA-…) حين يُفوتَر النوع على العميل بفئة غير خاضعة.</summary>
    public string? VatExemptionReasonCode { get; set; }
    /// <summary>
    /// «مبلغ مدفوع نيابة عن العميل»: حين يُحمَّل عليه يُقيَّد على حساب المدفوعات نيابة عن العملاء لا على الإيراد، ولو حُدِّد له سعر.
    /// </summary>
    public bool IsDisbursement { get; set; }
    public decimal? DefaultAmount { get; set; }
    public bool ShowToCustomer { get; set; }
    /// <summary>الحساب المحاسبي المرتبط (كود من شجرة الحسابات)؛ فارغ = حساب الإعدادات الافتراضي.</summary>
    public string? AccountCode { get; set; }
    /// <summary>نوع مزروع من النظام: يُعدَّل ويُعطَّل ولا يُحذف.</summary>
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
}
