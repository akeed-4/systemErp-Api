namespace ERP.Core.DTOs.CarShowroom;

/// <summary>الإعدادات الضريبية والحسابات يحددها محاسب المنشأة. حقل حساب فارغ في التعديل = يبقى كما هو.</summary>
public class CarShowroomSettingsDto
{
    public FeeBearer SaleFeesBorneBy { get; set; } = FeeBearer.Customer;
    public int ReservationDays { get; set; } = 7;
    public decimal StandardVatRate { get; set; } = 15m;
    public MarginBasis MarginBasis { get; set; } = MarginBasis.PurchasePrice;
    public DepositCancellationPolicy DepositCancellationPolicy { get; set; } = DepositCancellationPolicy.Refund;
    public bool RefundCustomerChargesOnReturn { get; set; }
    public string? VehicleInventoryAccount { get; set; }
    public string? VehiclesInTransitAccount { get; set; }
    public string? CarSalesRevenueAccount { get; set; }
    public string? CarCogsAccount { get; set; }
    public string? AccruedChargesAccount { get; set; }
    public string? TammWalletAccount { get; set; }
    public string? CustomerRechargeAccount { get; set; }
    public string? AddOnRevenueAccount { get; set; }
    public string? AddOnCostAccount { get; set; }
    public string? VehicleExpenseAccount { get; set; }
    public string? CustomerDepositsAccount { get; set; }
    public string? PrepaidExpensesAccount { get; set; }
    public string? PurchaseCancellationLossAccount { get; set; }
    public string? SupplierClaimsAccount { get; set; }
    public string? ForfeitedDepositsAccount { get; set; }
}

public class TammWalletTopUpDto
{
    public decimal Amount { get; set; }
    /// <summary>حساب البنك/الصندوق الذي شُحنت منه المحفظة.</summary>
    public string TreasuryAccountCode { get; set; } = string.Empty;
    /// <summary>طريقة الدفع المختارة (جدول طرق الدفع): حسابها المرتبط يُقدَّم على TreasuryAccountCode.</summary>
    public string? PaymentMethodCode { get; set; }
    public DateTime? Date { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
}

public class TammWalletReconcileDto
{
    /// <summary>رصيد المحفظة في كشف تم.</summary>
    public decimal StatementBalance { get; set; }
    public DateTime? Date { get; set; }
    /// <summary>true = يُسوّى الفرق بقيد على مصروف المعرض ليطابق الرصيدُ الدفتري الكشفَ.</summary>
    public bool AdjustDifference { get; set; }
    public string? Notes { get; set; }
}

public class TammWalletTransactionDto
{
    public Guid Id { get; set; }
    public DateTime Date { get; set; }
    public TammWalletTransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public string? TreasuryAccountCode { get; set; }
    public Guid? TammOperationId { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public Guid? JournalEntryId { get; set; }
    public decimal? StatementBalance { get; set; }
    public decimal? BookBalance { get; set; }
    public decimal? Difference { get; set; }
}

public class TammWalletDto
{
    public string AccountCode { get; set; } = string.Empty;
    /// <summary>الرصيد الدفتري (رصيد حساب المحفظة).</summary>
    public decimal Balance { get; set; }
    public TammWalletTransactionDto? LastReconciliation { get; set; }
    public PagedResultOfWalletTransactions Transactions { get; set; } = new();
}

public class PagedResultOfWalletTransactions
{
    public List<TammWalletTransactionDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
}

/// <summary>سيارة مباعة بنظام هامش الربح يختلف هامشها وضريبتها بين طريقتي الأساس (سعر الشراء فقط / التكلفة الكلية).</summary>
public class CarMarginBasisComparisonRowDto
{
    public Guid Id { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Vin { get; set; } = string.Empty;
    public string VehicleDescription { get; set; } = string.Empty;
    public decimal SellingPrice { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal TotalCost { get; set; }
    public decimal MarginOnPurchasePrice { get; set; }
    public decimal VatOnPurchasePrice { get; set; }
    public decimal MarginOnTotalCost { get; set; }
    public decimal VatOnTotalCost { get; set; }
    /// <summary>الضريبة التي احتُسبت فعلاً في الفاتورة.</summary>
    public decimal AppliedVat { get; set; }
    /// <summary>ضريبة أساس سعر الشراء − ضريبة أساس التكلفة الكلية.</summary>
    public decimal VatDifference { get; set; }
}

public class CreateVehicleReservationDto
{
    public Guid VehicleId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    /// <summary>فارغ = اليوم + مدة الحجز الافتراضية من الإعدادات.</summary>
    public DateTime? ExpiresAt { get; set; }
    public string? Notes { get; set; }
}

public class ExtendVehicleReservationDto
{
    public DateTime ExpiresAt { get; set; }
}

public class VehicleReservationDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid VehicleId { get; set; }
    public string? ChassisNumber { get; set; }
    public string? VehicleDescription { get; set; }
    public Guid? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public DateTime ReservedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public VehicleReservationStatus Status { get; set; }
    public bool IsManual { get; set; }
    public Guid? ContractId { get; set; }
    public Guid? DepositVoucherId { get; set; }
    /// <summary>انتهت مدته لكنه باقٍ لأن عليه عربوناً مفتوحاً أو عقداً قائماً.</summary>
    public bool IsOverdue { get; set; }
    public string? Notes { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? CloseReason { get; set; }
}

public class CreateTammOperationDto
{
    public TammOperationType Type { get; set; }
    public Guid? VehicleId { get; set; }
    /// <summary>رقم الشاسيه لسيارة ليست في النظام (استعلام قبل الشراء).</summary>
    public string? Vin { get; set; }
    public Guid? ContractId { get; set; }
    public decimal Fee { get; set; }
    public string? Reference { get; set; }
    public DateTime? RequestedAt { get; set; }
    public string? Notes { get; set; }
}

/// <summary>نقطة تحديث الحالة: تستقبل نتيجة العملية من الربط مع تم، أو حسمها اليدوي إن كانت مجهولة.</summary>
public class UpdateTammOperationStatusDto
{
    public TammOperationStatus Status { get; set; }
    /// <summary>الرسم الفعلي إن اختلف عن المُدخل عند الطلب.</summary>
    public decimal? Fee { get; set; }
    public string? Reference { get; set; }
    public string? FailureReason { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class TammOperationDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string OperationNumber { get; set; } = string.Empty;
    public TammOperationType Type { get; set; }
    public TammOperationStatus Status { get; set; }
    public Guid? VehicleId { get; set; }
    public string? Vin { get; set; }
    public Guid? ContractId { get; set; }
    public decimal Fee { get; set; }
    public string? Reference { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? FailureReason { get; set; }
    public bool ResolvedManually { get; set; }
    public string? ResolvedBy { get; set; }
    public string? Notes { get; set; }
    /// <summary>سطر المصروف الذي أنشأه نجاح العملية.</summary>
    public Guid? ChargeLineId { get; set; }
    public ChargeTreatment? ChargeTreatment { get; set; }
}

/// <summary>سطر يظهر للعميل في عرض السعر والفاتورة: رسم محمَّل عليه أو إضافة.</summary>
public class SaleCustomerChargeDto
{
    public Guid LineId { get; set; }
    public string Name { get; set; } = string.Empty;
    public ChargeStatus Status { get; set; }
    public decimal Price { get; set; }
    public decimal VatAmount { get; set; }
    public decimal Total { get; set; }
    public VatCategory VatCategory { get; set; }
}

/// <summary>
/// اللوحة الداخلية لعرض السعر وفاتورة البيع (لا تُطبع، ولمن يملك صلاحية رؤية التكلفة فقط): التكلفة الحالية،
/// المصاريف المتوقعة على المعرض، والربح المتوقع ونسبته. كل المبالغ قبل الضريبة وبالعملة الأساسية.
/// </summary>
public class SaleProfitPanelDto
{
    public Guid ContractId { get; set; }
    public Guid VehicleId { get; set; }
    // ما يراه العميل
    public decimal VehicleNetPrice { get; set; }
    public decimal VehicleVat { get; set; }
    public decimal CustomerChargesNet { get; set; }
    public decimal CustomerChargesVat { get; set; }
    public decimal CustomerTotal { get; set; }
    public List<SaleCustomerChargeDto> CustomerCharges { get; set; } = new();
    // الداخلي
    public decimal CurrentCost { get; set; }
    /// <summary>مصاريف تقديرية ستُضاف لتكلفة السيارة عند تحققها.</summary>
    public decimal PendingCapitalized { get; set; }
    /// <summary>مصاريف على المعرض (تقديرية وفعلية) مرتبطة بالسيارة.</summary>
    public decimal ShowroomExpenses { get; set; }
    /// <summary>تكلفة الرسوم والإضافات المحمّلة على العميل.</summary>
    public decimal CustomerChargesCost { get; set; }
    public decimal ExpectedProfit { get; set; }
    /// <summary>الربح المتوقع ÷ إجمالي الإيراد قبل الضريبة × 100.</summary>
    public decimal ExpectedProfitPercent { get; set; }
}
