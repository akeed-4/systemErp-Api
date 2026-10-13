namespace ERP.Core.DTOs.CarShowroom;

public class CreateLandedCostVoucherLineDto
{
    public Guid ExpenseTypeId { get; set; }
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public decimal VatAmount { get; set; }
    /// <summary>فارغ = طريقة النوع الافتراضية.</summary>
    public ChargeAllocationMethod? AllocationMethod { get; set; }
    /// <summary>فارغ = معالجة النوع الافتراضية. تغييرها يتطلب صلاحية charge-treatment-override.</summary>
    public ChargeTreatment? Treatment { get; set; }
    /// <summary>السطر التقديري الذي يحل هذا السطر محله؛ فارغ = مطابقة تلقائية بالنوع.</summary>
    public Guid? ReplacesLineId { get; set; }
    /// <summary>سيارات هذا السطر؛ فارغة = سيارات السند (VehicleIds). المبالغ تُرسل في التوزيع اليدوي فقط.</summary>
    public List<AdditionalChargeAllocationDto> Allocations { get; set; } = new();
}

public class CreateLandedCostVoucherDto
{
    public DateTime? Date { get; set; }
    public Guid SupplierId { get; set; }
    public string? SupplierInvoiceNumber { get; set; }
    /// <summary>فارغ = العملة الأساسية.</summary>
    public string? CurrencyCode { get; set; }
    /// <summary>سعر صرف تاريخ الفاتورة.</summary>
    public decimal ExchangeRate { get; set; } = 1;
    public Guid? ProcurementOrderId { get; set; }
    public string? Notes { get; set; }
    /// <summary>السيارات الافتراضية لكل الأسطر؛ فارغة مع أمر شراء = كل سيارات الأمر.</summary>
    public List<Guid> VehicleIds { get; set; } = new();
    public List<CreateLandedCostVoucherLineDto> Lines { get; set; } = new();
}

public class UpdateLandedCostVoucherDto : CreateLandedCostVoucherDto
{
}

public class LandedCostVoucherLineDto
{
    public Guid Id { get; set; }
    public int LineNo { get; set; }
    public Guid ExpenseTypeId { get; set; }
    public string ExpenseTypeCode { get; set; } = string.Empty;
    public string ExpenseTypeNameAr { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal CostAmount { get; set; }
    public ChargeTreatment Treatment { get; set; }
    public ChargeAllocationMethod AllocationMethod { get; set; }
    public Guid? ReplacesLineId { get; set; }
    public Guid? ReplacedLineId { get; set; }
    public decimal? EstimatedAmount { get; set; }
    public decimal? Variance { get; set; }
    public Guid? ChargeLineId { get; set; }
    public List<AdditionalChargeAllocationDto> Allocations { get; set; } = new();
}

public class LandedCostVoucherDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string? SupplierInvoiceNumber { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal ExchangeRate { get; set; }
    public Guid? ProcurementOrderId { get; set; }
    public string? ProcurementOrderNumber { get; set; }
    public LandedCostVoucherStatus Status { get; set; }
    public string? Notes { get; set; }
    public decimal Subtotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal BaseCostTotal { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public Guid? JournalEntryId { get; set; }
    public List<LandedCostVoucherLineDto> Lines { get; set; } = new();
}

/// <summary>معاينة التوزيع قبل الاعتماد: الأسطر بحصصها، وأثرها على تكلفة كل سيارة. لا يُحفظ شيء.</summary>
public class LandedCostPreviewDto
{
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal ExchangeRate { get; set; }
    public decimal Subtotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal BaseCostTotal { get; set; }
    public List<LandedCostVoucherLineDto> Lines { get; set; } = new();
    public List<LandedCostPreviewVehicleDto> Vehicles { get; set; } = new();
}

public class LandedCostPreviewVehicleDto
{
    public Guid VehicleId { get; set; }
    public string ChassisNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public VehicleStatus Status { get; set; }
    public decimal CurrentCost { get; set; }
    /// <summary>مجموع حصص السيارة من أسطر السند المُضافة للتكلفة.</summary>
    public decimal Capitalized { get; set; }
    /// <summary>ما يخرج من تكلفتها باستبدال أسطر فعلية مستحقة سابقاً (رسوم أمر الشراء).</summary>
    public decimal ReplacedActual { get; set; }
    public decimal CostAfter { get; set; }
}

/// <summary>التكلفة المتوقعة الكلية لأمر شراء: ثمن السيارات + المصاريف المُرسمَلة التقديرية والفعلية. بالعملة الأساسية.</summary>
public class ProcurementExpectedCostDto
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public int VehiclesCount { get; set; }
    public decimal PurchaseTotal { get; set; }
    public decimal EstimatedCharges { get; set; }
    public decimal ActualCharges { get; set; }
    public decimal ExpectedTotalCost { get; set; }
    /// <summary>مصاريف على المعرض أو على العميل مرتبطة بالأمر (لا تدخل التكلفة).</summary>
    public decimal NonCapitalizedCharges { get; set; }
}
