namespace ERP.Core.DTOs.CarShowroom;

/// <summary>كرت تكلفة السيارة: كل ما صُرف عليها من الطلب إلى البيع، المتوقع مقابل الفعلي. كل المبالغ بالعملة الأساسية.</summary>
public class VehicleCostCardDto
{
    public Guid VehicleId { get; set; }
    public string ChassisNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public VehicleStatus Status { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal AdditionalCosts { get; set; }
    public decimal PreparationCost { get; set; }
    public decimal TotalCost { get; set; }
    public decimal SellingPrice { get; set; }
    /// <summary>مصاريف تقديرية ستُضاف للتكلفة عند تحققها.</summary>
    public decimal EstimatedCapitalized { get; set; }
    /// <summary>التكلفة الحالية + التقديري المُرسمَل الذي لم يتحقق بعد.</summary>
    public decimal ExpectedTotalCost { get; set; }
    public decimal EstimatedExpensed { get; set; }
    public decimal ActualExpensed { get; set; }
    public decimal EstimatedRecharged { get; set; }
    public decimal ActualRecharged { get; set; }
    public List<VehicleCostCardLineDto> Lines { get; set; } = new();
}

public class VehicleCostCardLineDto
{
    public Guid LineId { get; set; }
    public DateTime Date { get; set; }
    public string ExpenseTypeCode { get; set; } = string.Empty;
    public string ExpenseTypeNameAr { get; set; } = string.Empty;
    public ExpenseStage Stage { get; set; }
    public ChargeTreatment Treatment { get; set; }
    public ChargeStatus Status { get; set; }
    public ChargeSource Source { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public Guid? DocumentId { get; set; }
    public string? DocumentNumber { get; set; }
    public string? SupplierName { get; set; }
    public string? Description { get; set; }
    /// <summary>حصة السيارة التقديرية: مبلغ السطر التقديري، أو مبلغ السطر الذي حلّ هذا الفعلي محلّه.</summary>
    public decimal? EstimatedAmount { get; set; }
    public decimal? ActualAmount { get; set; }
}
