namespace ERP.Core.DTOs.CarShowroom;

public partial class AdditionalChargeLineDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public Guid? DocumentId { get; set; }
    public string? DocumentNumber { get; set; }
    public Guid ExpenseTypeId { get; set; }
    public string ExpenseTypeCode { get; set; } = string.Empty;
    public string ExpenseTypeNameAr { get; set; } = string.Empty;
    public ExpenseStage Stage { get; set; }
    public DateTime Date { get; set; }
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal ExchangeRate { get; set; }
    public decimal BaseAmount { get; set; }
    public VatCategory VatCategory { get; set; }
    public decimal VatAmount { get; set; }
    public bool IsVatRecoverable { get; set; }
    public bool IsInputTax { get; set; }
    public decimal CostAmount { get; set; }
    public decimal? EstimatedCostAmount { get; set; }
    public decimal? SalePrice { get; set; }
    public ChargeTreatment Treatment { get; set; }
    public ChargeStatus Status { get; set; }
    public ChargeSource Source { get; set; }
    public ChargeAllocationMethod AllocationMethod { get; set; }
    public string? PaymentAccountCode { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string? SupplierInvoiceNumber { get; set; }
    public string? Notes { get; set; }
    public Guid? TammOperationId { get; set; }
    public Guid? ReplacesLineId { get; set; }
    public Guid? JournalEntryId { get; set; }
    public List<AdditionalChargeAllocationDto> Allocations { get; set; } = new();
}
