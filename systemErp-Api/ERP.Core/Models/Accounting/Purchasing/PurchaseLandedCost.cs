namespace ERP.Core.Models.Accounting;

public enum PurchaseLandedCostStatus
{
    Draft = 1,
    Approved = 2,
    Cancelled = 3
}

/// <summary>طريقة توزيع مصروف على أسطر الأصناف في فواتير الشراء المرتبطة.</summary>
public enum PurchaseCostAllocationMethod
{
    ByValue = 1,
    ByQuantity = 2,
    ByWeight = 3,
    Manual = 4
}

/// <summary>الجهة المستحقة لمبلغ السند: مورد (ومنه المخلّص الجمركي)، أو حساب دفع (بنك/صندوق)، أو مستحقات حتى تصل الفاتورة.</summary>
public enum PurchaseCostPayee
{
    Supplier = 1,
    PaymentAccount = 2,
    Accrued = 3
}

/// <summary>
/// أنواع التكاليف الإضافية للمشتريات العامة (شحن، جمارك، تخليص...): كتالوج مستقل عن أنواع مصاريف السيارات.
/// الفئة الضريبية وقابلية الاسترداد قيم ابتدائية يحددها محاسب المنشأة.
/// </summary>
public class PurchaseExpenseType : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public VatCategory VatCategory { get; set; } = VatCategory.Standard;
    /// <summary>ضريبة السطر (أو مبلغه كله إن كان ضريبة) تُقيَّد ضريبة مدخلات؛ غير المستردة تدخل في التكلفة.</summary>
    public bool IsVatRecoverable { get; set; } = true;
    /// <summary>المبلغ كله ضريبة (مثل ضريبة الاستيراد المدفوعة للجمارك): لا يدخل التكلفة ولا تُسجَّل عليه ضريبة.</summary>
    public bool IsInputTax { get; set; }
    public PurchaseCostAllocationMethod DefaultAllocationMethod { get; set; } = PurchaseCostAllocationMethod.ByValue;
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// سند التكاليف الإضافية للمشتريات العامة: فاتورة شحن/جمارك/تخليص تُحمَّل على تكلفة أصناف فواتير شراء مرحّلة.
/// عند الاعتماد ترتفع تكلفة حركات الشراء بحصصها، والمخزون يُقيَّد بتغيّر قيمته الفعلي وما بيع منه على تكلفة المبيعات.
/// </summary>
public class PurchaseLandedCost : BaseEntity
{
    public string VoucherNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public PurchaseCostPayee Payee { get; set; } = PurchaseCostPayee.Supplier;
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string? PaymentAccountCode { get; set; }
    public string? SupplierInvoiceNumber { get; set; }
    public string CurrencyCode { get; set; } = "SAR";
    public decimal ExchangeRate { get; set; } = 1;
    public PurchaseLandedCostStatus Status { get; set; } = PurchaseLandedCostStatus.Draft;
    public string? Notes { get; set; }

    // بعملة السند
    public decimal Subtotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    // بالعملة الأساسية
    /// <summary>ما يُحمَّل على تكلفة الأصناف (المبالغ + الضريبة غير المستردة).</summary>
    public decimal BaseCostTotal { get; set; }
    public decimal BaseInputVat { get; set; }

    // أثر الاعتماد
    /// <summary>ما دخل حساب المخزون فعلاً (تغيّر قيمة الأصناف).</summary>
    public decimal InventoryAmount { get; set; }
    /// <summary>الباقي: حصة ما بيع (تكلفة المبيعات) أو ما لا تمتصه طريقة التكلفة (فرق السعر).</summary>
    public decimal ExpensedAmount { get; set; }
    public string? ExpensedAccountCode { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public Guid? JournalEntryId { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelledBy { get; set; }
    public Guid? CancelJournalEntryId { get; set; }

    public virtual ICollection<PurchaseLandedCostInvoice> Invoices { get; set; } = new List<PurchaseLandedCostInvoice>();
    public virtual ICollection<PurchaseLandedCostLine> Lines { get; set; } = new List<PurchaseLandedCostLine>();
}

/// <summary>فاتورة شراء مرتبطة بالسند.</summary>
public class PurchaseLandedCostInvoice : BaseEntity
{
    public Guid VoucherId { get; set; }
    public Guid InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;

    public virtual PurchaseLandedCost Voucher { get; set; } = null!;
}

public class PurchaseLandedCostLine : BaseEntity
{
    public Guid VoucherId { get; set; }
    public int LineNo { get; set; }
    public Guid ExpenseTypeId { get; set; }
    public string ExpenseTypeCode { get; set; } = string.Empty;
    public string ExpenseTypeNameAr { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>المبلغ قبل الضريبة بعملة السند.</summary>
    public decimal Amount { get; set; }
    /// <summary>ضريبة مقطوعة تُدخل يدوياً بعملة السند (لا نسبة مفروضة).</summary>
    public decimal VatAmount { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal BaseVatAmount { get; set; }
    /// <summary>ما يُحمَّل على الأصناف بالعملة الأساسية.</summary>
    public decimal CostAmount { get; set; }
    /// <summary>ضريبة المدخلات المستردة بالعملة الأساسية.</summary>
    public decimal InputVatAmount { get; set; }
    public PurchaseCostAllocationMethod AllocationMethod { get; set; }

    public virtual PurchaseLandedCost Voucher { get; set; } = null!;
    public virtual ICollection<PurchaseLandedCostAllocation> Allocations { get; set; } = new List<PurchaseLandedCostAllocation>();
}

/// <summary>حصة سطر صنف في فاتورة شراء من سطر السند، بالعملة الأساسية.</summary>
public class PurchaseLandedCostAllocation : BaseEntity
{
    public Guid LineId { get; set; }
    public Guid InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public Guid InvoiceItemId { get; set; }
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    /// <summary>الكمية المشتراة ناقص المرتجع وقت الحساب.</summary>
    public decimal NetQuantity { get; set; }
    public decimal Amount { get; set; }
    /// <summary>زيادة تكلفة الوحدة في حركة الشراء (تُسجَّل عند الاعتماد ليُعكس بها الإلغاء تماماً).</summary>
    public decimal UnitCostIncrease { get; set; }
    public Guid? StockMovementId { get; set; }

    public virtual PurchaseLandedCostLine Line { get; set; } = null!;
}
