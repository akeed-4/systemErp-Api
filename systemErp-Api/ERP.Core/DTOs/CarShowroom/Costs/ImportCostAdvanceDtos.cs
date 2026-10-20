namespace ERP.Core.DTOs.CarShowroom;

public class CreateImportCostAdvanceDto
{
    public DateTime? Date { get; set; }
    public Guid ProcurementOrderId { get; set; }
    public Guid ExpenseTypeId { get; set; }
    public string? Description { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierInvoiceNumber { get; set; }
    /// <summary>فارغ = العملة الأساسية.</summary>
    public string? CurrencyCode { get; set; }
    public decimal ExchangeRate { get; set; } = 1;
    /// <summary>قبل الضريبة بعملة المستند.</summary>
    public decimal Amount { get; set; }
    public decimal VatAmount { get; set; }
    /// <summary>طريقة الدفع الفورية (بنك/صندوق) من جدول طرق الدفع: حسابها المرتبط يُقيَّد دائناً.</summary>
    public string PaymentMethodCode { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class UpdateImportCostAdvanceDto : CreateImportCostAdvanceDto
{
}

public class ImportCostAdvanceDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
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
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal ExchangeRate { get; set; }
    public decimal Amount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal CostAmount { get; set; }
    public decimal InputVatAmount { get; set; }
    public string PaymentMethodCode { get; set; } = string.Empty;
    public string PaymentAccountCode { get; set; } = string.Empty;
    public decimal SettledAmount { get; set; }
    public decimal SettledCost { get; set; }
    /// <summary>المتبقي للتسوية بعملة المستند (قبل الضريبة) وبالعملة الأساسية.</summary>
    public decimal RemainingAmount { get; set; }
    public decimal RemainingCost { get; set; }
    public ImportCostAdvanceStatus Status { get; set; }
    public string? Notes { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public Guid? JournalEntryId { get; set; }
}

/// <summary>رصيد تكاليف الاستيراد تحت التسوية لأمر شراء (بالعملة الأساسية).</summary>
public class ImportCostAdvanceBalanceDto
{
    public Guid ProcurementOrderId { get; set; }
    public string ProcurementOrderNumber { get; set; } = string.Empty;
    public string? SupplierName { get; set; }
    public ProcurementOrderStatus OrderStatus { get; set; }
    public int AdvancesCount { get; set; }
    public decimal Paid { get; set; }
    public decimal Settled { get; set; }
    public decimal Remaining { get; set; }
    public int VehiclesCount { get; set; }
    public int ArrivedVehicles { get; set; }
    /// <summary>الأمر مكتمل (مستلم/مفوتر/مقفل أو وصلت كل سياراته).</summary>
    public bool OrderCompleted { get; set; }
    /// <summary>أمر مكتمل ما زال عليه رصيد: يحتاج تسوية أو مراجعة.</summary>
    public bool NeedsAttention { get; set; }
}
