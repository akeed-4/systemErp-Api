namespace ERP.Core.DTOs.CarShowroom;

public partial class ExpenseTypeDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public ExpenseStage Stage { get; set; }
    public ChargeTreatment DefaultTreatment { get; set; }
    public ChargeAllocationMethod DefaultAllocationMethod { get; set; }
    public VatCategory VatCategory { get; set; }
    public bool IsVatRecoverable { get; set; }
    public bool IsInputTax { get; set; }
    public string? VatExemptionReasonCode { get; set; }
    public bool IsDisbursement { get; set; }
    public decimal? DefaultAmount { get; set; }
    public bool ShowToCustomer { get; set; }
    public string? AccountCode { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; }
}
