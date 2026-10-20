namespace ERP.Core.DTOs.CarShowroom;

public partial class CreateAdditionalChargeLineDto
{
    /// <summary>car_procurement_order | car_sales_contract | invoice | vehicle</summary>
    public string DocumentType { get; set; } = ChargeDocumentTypes.Vehicle;
    public Guid? DocumentId { get; set; }
    public Guid ExpenseTypeId { get; set; }
    public DateTime? Date { get; set; }
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    /// <summary>فارغ = العملة الأساسية.</summary>
    public string? CurrencyCode { get; set; }
    public decimal ExchangeRate { get; set; } = 1;
    public decimal VatAmount { get; set; }
    /// <summary>لسطر على العميل: سعر بيعه قبل الضريبة بالعملة الأساسية؛ فارغ = مبلغ السطر (يُمرَّر بتكلفته).</summary>
    public decimal? SalePrice { get; set; }
    /// <summary>فارغ = معالجة النوع الافتراضية. تغييرها يتطلب صلاحية charge-treatment-override.</summary>
    public ChargeTreatment? Treatment { get; set; }
    public ChargeStatus Status { get; set; } = ChargeStatus.Estimated;
    /// <summary>فارغ = طريقة النوع الافتراضية.</summary>
    public ChargeAllocationMethod? AllocationMethod { get; set; }
    /// <summary>حساب الدفع (بنك/صندوق) لمصروف دُفع بلا مورد؛ فارغ = حساب المستحقات.</summary>
    public string? PaymentAccountCode { get; set; }
    /// <summary>طريقة الدفع المختارة (جدول طرق الدفع): حسابها المرتبط يُقدَّم على PaymentAccountCode.</summary>
    public string? PaymentMethodCode { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierInvoiceNumber { get; set; }
    public string? Notes { get; set; }
    /// <summary>سبب التعديل؛ إلزامي حين يمسّ السطر تكلفة سيارة مباعة (قيد تعديل موثق، بصلاحية sold-vehicle-adjustment).</summary>
    public string? AdjustmentReason { get; set; }
    public List<AdditionalChargeAllocationDto> Allocations { get; set; } = new();
}
