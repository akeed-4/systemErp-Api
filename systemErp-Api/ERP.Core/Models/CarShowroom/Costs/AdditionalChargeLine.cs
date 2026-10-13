namespace ERP.Core.Models.CarShowroom;

/// <summary>
/// سطر مصروف/تكلفة إضافية مرتبط بمستند (أمر شراء، عقد بيع، فاتورة) أو بسيارة مباشرة، وموزَّع على سيارة أو أكثر.
/// المبالغ بعملة السطر، و<see cref="BaseAmount"/> و<see cref="CostAmount"/> بالعملة الأساسية.
/// لا يدخل تكلفة السيارة إلا السطر الفعلي (Actual) بمعالجة الإضافة للتكلفة (Capitalize).
/// </summary>
public class AdditionalChargeLine : BaseEntity
{
    public string DocumentType { get; set; } = ChargeDocumentTypes.Vehicle;
    public Guid? DocumentId { get; set; }
    public string? DocumentNumber { get; set; }

    public Guid ExpenseTypeId { get; set; }
    public string ExpenseTypeCode { get; set; } = string.Empty;
    public string ExpenseTypeNameAr { get; set; } = string.Empty;
    public ExpenseStage Stage { get; set; }
    public DateTime Date { get; set; }
    public string? Description { get; set; }

    /// <summary>المبلغ قبل الضريبة بعملة السطر.</summary>
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "SAR";
    public decimal ExchangeRate { get; set; } = 1;
    public decimal BaseAmount { get; set; }
    public VatCategory VatCategory { get; set; } = VatCategory.Standard;
    /// <summary>ضريبة السطر بعملته (يُدخلها المستخدم من فاتورة المورد).</summary>
    public decimal VatAmount { get; set; }
    public bool IsVatRecoverable { get; set; } = true;
    public bool IsInputTax { get; set; }
    /// <summary>ما يُحمَّل فعلاً كتكلفة/مصروف بالعملة الأساسية: المبلغ + الضريبة غير المستردة، وصفر لضريبة مدخلات مستردة.</summary>
    public decimal CostAmount { get; set; }
    /// <summary>ما كان مقدَّراً للسطر قبل أن يتحقق (حين يُحوَّل التقديري نفسه إلى فعلي بمبلغ مختلف): سجل بالقيمتين.</summary>
    public decimal? EstimatedCostAmount { get; set; }

    /// <summary>
    /// لسطر محمَّل على العميل: ما يُفوتَر عليه قبل الضريبة بالعملة الأساسية (سعر بيع الإضافة). فارغ = مبلغ السطر كما هو (رسم يُمرَّر بتكلفته).
    /// </summary>
    public decimal? SalePrice { get; set; }
    public ChargeTreatment Treatment { get; set; }
    public ChargeStatus Status { get; set; } = ChargeStatus.Estimated;
    public ChargeSource Source { get; set; } = ChargeSource.Manual;
    public ChargeAllocationMethod AllocationMethod { get; set; } = ChargeAllocationMethod.Equal;

    /// <summary>حساب الدفع (بنك/صندوق) لمصروف دُفع نقداً بلا مورد. فارغ بلا مورد = يُقيَّد على حساب المستحقات.</summary>
    public string? PaymentAccountCode { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string? SupplierInvoiceNumber { get; set; }
    public string? Notes { get; set; }

    /// <summary>عملية تم التي أنشأت السطر؛ فريد لكل منشأة فلا يتكرر المصروف للعملية نفسها.</summary>
    public Guid? TammOperationId { get; set; }
    /// <summary>السطر التقديري الذي حلّ هذا السطر الفعلي محلّه.</summary>
    public Guid? ReplacesLineId { get; set; }
    public Guid? JournalEntryId { get; set; }

    public virtual ICollection<AdditionalChargeAllocation> Allocations { get; set; } = new List<AdditionalChargeAllocation>();
}
