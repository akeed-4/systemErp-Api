namespace ERP.Core.DTOs.CarShowroom;

public partial class CreateExpenseTypeDto
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public ExpenseStage Stage { get; set; } = ExpenseStage.Purchase;
    public ChargeTreatment DefaultTreatment { get; set; } = ChargeTreatment.Capitalize;
    public ChargeAllocationMethod DefaultAllocationMethod { get; set; } = ChargeAllocationMethod.Equal;
    public VatCategory VatCategory { get; set; } = VatCategory.Standard;
    public bool IsVatRecoverable { get; set; } = true;
    public bool IsInputTax { get; set; }
    public string? VatExemptionReasonCode { get; set; }
    public bool IsDisbursement { get; set; }
    public decimal? DefaultAmount { get; set; }
    public bool ShowToCustomer { get; set; }
    public string? AccountCode { get; set; }
    public bool IsActive { get; set; } = true;
}
