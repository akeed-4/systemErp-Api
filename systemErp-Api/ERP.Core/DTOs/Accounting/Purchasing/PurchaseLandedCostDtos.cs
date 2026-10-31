using ERP.Core.Models.Accounting;
using ERP.Core.Models.Shared;

namespace ERP.Core.DTOs.Accounting;

public class PurchaseExpenseTypeDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public VatCategory VatCategory { get; set; }
    public bool IsVatRecoverable { get; set; }
    public bool IsInputTax { get; set; }
    public PurchaseCostAllocationMethod DefaultAllocationMethod { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; }
}

public class SavePurchaseExpenseTypeDto
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public VatCategory VatCategory { get; set; } = VatCategory.Standard;
    public bool IsVatRecoverable { get; set; } = true;
    public bool IsInputTax { get; set; }
    public PurchaseCostAllocationMethod DefaultAllocationMethod { get; set; } = PurchaseCostAllocationMethod.ByValue;
    public bool IsActive { get; set; } = true;
}

/// <summary>إنشاء/تعديل مسودة السند، وهو نفسه طلب المعاينة.</summary>
public class SavePurchaseLandedCostDto
{
    public DateTime? Date { get; set; }
    public List<Guid> InvoiceIds { get; set; } = new();
    public PurchaseCostPayee Payee { get; set; } = PurchaseCostPayee.Supplier;
    public Guid? SupplierId { get; set; }
    public string? PaymentAccountCode { get; set; }
    public string? SupplierInvoiceNumber { get; set; }
    public string? CurrencyCode { get; set; }
    public decimal ExchangeRate { get; set; } = 1;
    public string? Notes { get; set; }
    public List<SavePurchaseLandedCostLineDto> Lines { get; set; } = new();
}

public class SavePurchaseLandedCostLineDto
{
    public Guid ExpenseTypeId { get; set; }
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    /// <summary>ضريبة مقطوعة بعملة السند؛ صفر إن لم تكن.</summary>
    public decimal VatAmount { get; set; }
    /// <summary>فارغ = طريقة النوع الافتراضية.</summary>
    public PurchaseCostAllocationMethod? AllocationMethod { get; set; }
    /// <summary>التوزيع اليدوي: مبلغ كل سطر صنف بالعملة الأساسية، ومجموعها يساوي ما يُحمَّل من السطر.</summary>
    public List<ManualPurchaseCostShareDto>? Manual { get; set; }
}

public class ManualPurchaseCostShareDto
{
    public Guid InvoiceItemId { get; set; }
    public decimal Amount { get; set; }
}

public class PurchaseLandedCostDto
{
    public Guid Id { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public PurchaseCostPayee Payee { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string? PaymentAccountCode { get; set; }
    public string? SupplierInvoiceNumber { get; set; }
    public string CurrencyCode { get; set; } = "SAR";
    public decimal ExchangeRate { get; set; }
    public PurchaseLandedCostStatus Status { get; set; }
    public string? Notes { get; set; }
    public decimal Subtotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal BaseCostTotal { get; set; }
    public decimal BaseInputVat { get; set; }
    public decimal InventoryAmount { get; set; }
    public decimal ExpensedAmount { get; set; }
    public string? ExpensedAccountCode { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public Guid? JournalEntryId { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelledBy { get; set; }
    public Guid? CancelJournalEntryId { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<PurchaseLandedCostInvoiceDto> Invoices { get; set; } = new();
    public List<PurchaseLandedCostLineDto> Lines { get; set; } = new();
    /// <summary>أثر السند على تكلفة كل صنف (مجموع حصصه من كل الأسطر).</summary>
    public List<PurchaseLandedCostItemDto> Items { get; set; } = new();
}

public class PurchaseLandedCostInvoiceDto
{
    public Guid InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
}

public class PurchaseLandedCostLineDto
{
    public int LineNo { get; set; }
    public Guid ExpenseTypeId { get; set; }
    public string ExpenseTypeCode { get; set; } = string.Empty;
    public string ExpenseTypeNameAr { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal BaseVatAmount { get; set; }
    public decimal CostAmount { get; set; }
    public decimal InputVatAmount { get; set; }
    public PurchaseCostAllocationMethod AllocationMethod { get; set; }
    public List<PurchaseLandedCostAllocationDto> Allocations { get; set; } = new();
}

public class PurchaseLandedCostAllocationDto
{
    public Guid InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public Guid InvoiceItemId { get; set; }
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public decimal NetQuantity { get; set; }
    public decimal Amount { get; set; }
    public decimal UnitCostIncrease { get; set; }
}

public class PurchaseLandedCostItemDto
{
    public Guid InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public Guid InvoiceItemId { get; set; }
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public decimal NetQuantity { get; set; }
    /// <summary>تكلفة الوحدة في الفاتورة بالعملة الأساسية قبل السند.</summary>
    public decimal UnitCost { get; set; }
    public decimal Capitalized { get; set; }
    public decimal UnitCostAfter { get; set; }
}
