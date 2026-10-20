namespace ERP.Core.Models.CarShowroom;

public enum ImportCostAdvanceStatus
{
    Draft = 1,
    Open = 2,
    PartiallySettled = 3,
    Settled = 4,
    Cancelled = 5
}

/// <summary>
/// تكلفة استيراد مدفوعة مقدماً لسيارات أمر شراء لم تصل بعد (شحن، تأمين، تخليص...). مستقلة عن «المصروفات المدفوعة مقدماً»
/// التي تُطفأ شهرياً: هذه تُسوّى بسحبها في سند التكاليف الإضافية فتُوزَّع على سيارات الأمر.
/// الاعتماد: مدين «تكاليف استيراد تحت التسوية» بالتكلفة + مدين ضريبة المدخلات المستردة، دائن حساب طريقة الدفع.
/// المسوّى مشتق من أسطر السندات المعتمدة المرتبطة بها، فإلغاء السند يعيد المبلغ للرصيد تلقائياً.
/// </summary>
public class ImportCostAdvance : BaseEntity
{
    public string Number { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public Guid ProcurementOrderId { get; set; }
    public string ProcurementOrderNumber { get; set; } = string.Empty;

    public Guid ExpenseTypeId { get; set; }
    public string ExpenseTypeCode { get; set; } = string.Empty;
    public string ExpenseTypeNameAr { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string? SupplierInvoiceNumber { get; set; }

    public string CurrencyCode { get; set; } = "SAR";
    public decimal ExchangeRate { get; set; } = 1;
    /// <summary>المبلغ قبل الضريبة بعملة المستند.</summary>
    public decimal Amount { get; set; }
    /// <summary>الضريبة بعملة المستند.</summary>
    public decimal VatAmount { get; set; }
    /// <summary>ما يُقيَّد على حساب التسوية بالعملة الأساسية: المبلغ + الضريبة غير المستردة.</summary>
    public decimal CostAmount { get; set; }
    /// <summary>ضريبة المدخلات المستردة بالعملة الأساسية (تُقيَّد عند الاعتماد كاملة ولا تنتقل مع التسوية).</summary>
    public decimal InputVatAmount { get; set; }

    /// <summary>طريقة الدفع (جدول طرق الدفع)، وحسابها المرتبط الذي يُقيَّد دائناً.</summary>
    public string PaymentMethodCode { get; set; } = string.Empty;
    public string PaymentAccountCode { get; set; } = string.Empty;

    /// <summary>المسوّى بعملة المستند (قبل الضريبة) وبالعملة الأساسية (من التكلفة)، من السندات المعتمدة.</summary>
    public decimal SettledAmount { get; set; }
    public decimal SettledCost { get; set; }

    public ImportCostAdvanceStatus Status { get; set; } = ImportCostAdvanceStatus.Draft;
    public string? Notes { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public Guid? JournalEntryId { get; set; }
}
