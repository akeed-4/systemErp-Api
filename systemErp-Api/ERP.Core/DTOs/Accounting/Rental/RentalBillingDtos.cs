namespace ERP.Core.DTOs.Accounting;

public class RentalSettingsDto
{
    public decimal LateFeeFactor { get; set; }
    public decimal LateGraceHours { get; set; }
    public bool RequireDepositBeforeDelivery { get; set; }
}

public class RentalChargeTypeDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? RevenueAccountCode { get; set; }
    public string DefaultTaxTreatment { get; set; } = string.Empty;
    public bool RequiresClassification { get; set; }
    public bool IsActive { get; set; }
}

public class SaveRentalChargeTypeDto
{
    public string? Code { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? RevenueAccountCode { get; set; }
    public string? DefaultTaxTreatment { get; set; }
    public bool RequiresClassification { get; set; }
    public bool IsActive { get; set; } = true;
}

public class SaveRentalChargeDto
{
    public Guid ContractId { get; set; }
    public Guid? LineId { get; set; }
    public Guid ChargeTypeId { get; set; }
    public string? Description { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    /// <summary>فارغ = التصنيف الافتراضي للنوع (أو بلا تصنيف إن كان النوع يتطلب تصنيفاً).</summary>
    public string? TaxTreatment { get; set; }
    public string? Notes { get; set; }
}

/// <summary>اعتماد الرسم (مع تصنيفه إن لم يُصنَّف) أو تعديل مبلغه قبل الاعتماد.</summary>
public class ApproveRentalChargeDto
{
    public string? TaxTreatment { get; set; }
}

public class RentalChargeDto
{
    public Guid Id { get; set; }
    public Guid ContractId { get; set; }
    public string? ContractNumber { get; set; }
    public Guid? LineId { get; set; }
    public string? EquipmentCode { get; set; }
    public Guid ChargeTypeId { get; set; }
    public string? ChargeTypeName { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public string? TaxTreatment { get; set; }
    public bool RequiresClassification { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? BillingId { get; set; }
    public string? WaiveReason { get; set; }
    public string? WaivedBy { get; set; }
    public string? ApprovedBy { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class RentalBillingPreviewLineDto
{
    public Guid? ContractLineId { get; set; }
    public Guid? ChargeId { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime? PeriodFrom { get; set; }
    public DateTime? PeriodTo { get; set; }
    public decimal Amount { get; set; }
    public string TaxTreatment { get; set; } = string.Empty;
    public decimal Vat { get; set; }
}

public class RentalBillingPreviewDto
{
    public Guid ContractId { get; set; }
    public DateTime Until { get; set; }
    public List<RentalBillingPreviewLineDto> Lines { get; set; } = new();
    public decimal Net { get; set; }
    public decimal Vat { get; set; }
    public decimal Total { get; set; }
    /// <summary>رسوم معلّقة لن تُفوتر قبل اعتمادها.</summary>
    public int PendingCharges { get; set; }
}

public class BillRentalContractDto
{
    /// <summary>لحظة الفوترة حتى (فارغ = الآن؛ المستقبل للفوترة مقدماً ضمن النهاية المخططة).</summary>
    public DateTime? Until { get; set; }
    public string? Notes { get; set; }
}

public class RentalBillingDto
{
    public Guid Id { get; set; }
    public string BillingNumber { get; set; } = string.Empty;
    public Guid ContractId { get; set; }
    public string? ContractNumber { get; set; }
    public string? CustomerName { get; set; }
    public DateTime BilledUntil { get; set; }
    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public decimal RentAmount { get; set; }
    public decimal ChargesAmount { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? CancelReason { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}
