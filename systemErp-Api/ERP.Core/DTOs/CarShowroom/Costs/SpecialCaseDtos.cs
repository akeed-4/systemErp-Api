namespace ERP.Core.DTOs.CarShowroom;

public class VehicleCostAdjustmentDto
{
    public Guid Id { get; set; }
    public string AdjustmentNumber { get; set; } = string.Empty;
    public Guid VehicleId { get; set; }
    public Guid? ContractId { get; set; }
    public Guid? ChargeLineId { get; set; }
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? ApprovedBy { get; set; }
    public Guid? JournalEntryId { get; set; }
}

/// <summary>سبب إجراء على سطر مصروف؛ إلزامي حين يمسّ تكلفة سيارة مباعة (قيد تعديل موثق).</summary>
public class ChargeActionReasonDto
{
    public string? Reason { get; set; }
}

/// <summary>إلغاء شراء بعد مصاريف: ما صُرف فعلاً على سياراته يتحول لخسارة أو لمطالبة على المورد.</summary>
public class SettleCancelledChargesDto
{
    /// <summary>loss | supplier_claim</summary>
    public string Treatment { get; set; } = "loss";
    public string? Notes { get; set; }
}

public class CancelVehicleReservationDto
{
    public string? Reason { get; set; }
    /// <summary>لحجز عليه عربون مفتوح: refund | forfeit. فارغ = سياسة إعدادات المعرض.</summary>
    public string? DepositAction { get; set; }
    /// <summary>حساب البنك/الصندوق الذي يُرد منه العربون؛ فارغ = الحساب الذي قُبض فيه.</summary>
    public string? RefundTreasuryAccountCode { get; set; }
}

public class ReturnCarSaleDto
{
    public string Reason { get; set; } = string.Empty;
    public PaymentMethod? RefundPaymentMethod { get; set; }
    /// <summary>هل تُرد للعميل الرسوم والإضافات المحمّلة عليه؟ فارغ = إعداد المعرض.</summary>
    public bool? RefundCustomerCharges { get; set; }
    public DateTime? ReturnDate { get; set; }
}

public class CreatePrepaidExpenseDto
{
    public Guid ExpenseTypeId { get; set; }
    public string? Description { get; set; }
    public DateTime StartDate { get; set; }
    public int Months { get; set; } = 12;
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public Guid? SupplierId { get; set; }
    /// <summary>حساب الدفع (بنك/صندوق/رصيد تم) إن لم يكن لمورد.</summary>
    public string? PaymentAccountCode { get; set; }
}

public class PrepaidExpenseAmortizationDto
{
    public Guid Id { get; set; }
    public string Period { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public Guid? JournalEntryId { get; set; }
}

public class PrepaidExpenseDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Number { get; set; } = string.Empty;
    public Guid ExpenseTypeId { get; set; }
    public string ExpenseTypeNameAr { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime StartDate { get; set; }
    public int Months { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal AmortizedAmount { get; set; }
    public decimal RemainingAmount => TotalAmount - AmortizedAmount;
    public Guid? SupplierId { get; set; }
    public string? PaymentAccountCode { get; set; }
    public PrepaidExpenseStatus Status { get; set; }
    public Guid? JournalEntryId { get; set; }
    public List<PrepaidExpenseAmortizationDto> Amortizations { get; set; } = new();
}

public class AmortizePrepaidExpensesDto
{
    /// <summary>الشهر المراد إطفاؤه بصيغة yyyy-MM؛ فارغ = الشهر الحالي.</summary>
    public string? Period { get; set; }
}

public class AmortizePrepaidExpensesResultDto
{
    public string Period { get; set; } = string.Empty;
    /// <summary>عدد المصروفات التي أُطفئ لها هذا الشهر الآن (صفر إن كان أُطفئ من قبل).</summary>
    public int Posted { get; set; }
    public decimal Amount { get; set; }
}

// ---------------- تقارير المصاريف والتكاليف ----------------
/// <summary>ربح السيارة المباعة بعد كل المصاريف. المبالغ قبل الضريبة وبالعملة الأساسية.</summary>
public class CarVehicleNetProfitRowDto
{
    public Guid Id { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Vin { get; set; } = string.Empty;
    public string VehicleDescription { get; set; } = string.Empty;
    public string BuyerName { get; set; } = string.Empty;
    public decimal VehicleRevenue { get; set; }
    public decimal CustomerChargesRevenue { get; set; }
    public decimal CostAtSale { get; set; }
    /// <summary>مصاريف فعلية وصلت بعد البيع وحُمّلت على تكلفة المبيعات.</summary>
    public decimal PostSaleAdjustments { get; set; }
    public decimal ShowroomExpenses { get; set; }
    public decimal CustomerChargesCost { get; set; }
    public decimal NetProfit { get; set; }
    public decimal NetProfitPercent { get; set; }
}

public class CarEstimatedVsActualRowDto
{
    public Guid Id { get; set; }
    public DateTime Date { get; set; }
    public string ExpenseTypeCode { get; set; } = string.Empty;
    public string ExpenseTypeNameAr { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string? DocumentNumber { get; set; }
    public ChargeTreatment Treatment { get; set; }
    public ChargeStatus Status { get; set; }
    public decimal Estimated { get; set; }
    /// <summary>فارغ لتقدير لم يتحقق بعد.</summary>
    public decimal? Actual { get; set; }
    /// <summary>الفعلي − التقديري.</summary>
    public decimal? Variance { get; set; }
}

public class CarTammMonthlyFeesRowDto
{
    /// <summary>yyyy-MM|كود النوع.</summary>
    public string Key { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public string ExpenseTypeCode { get; set; } = string.Empty;
    public string ExpenseTypeNameAr { get; set; } = string.Empty;
    public int OperationsCount { get; set; }
    public decimal TotalFees { get; set; }
}

public class CarUnpurchasedVehicleExpenseRowDto
{
    public Guid Id { get; set; }
    public DateTime Date { get; set; }
    public string? Vin { get; set; }
    public string ExpenseTypeNameAr { get; set; } = string.Empty;
    public ChargeSource Source { get; set; }
    public string? DocumentNumber { get; set; }
    public decimal Amount { get; set; }
    /// <summary>true = شراء أُلغي بعد الصرف؛ false = استعلام على سيارة لم تُشترَ.</summary>
    public bool PurchaseCancelled { get; set; }
}

public class CarVehicleInTransitRowDto
{
    public Guid Id { get; set; }
    public string ChassisNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? OrderNumber { get; set; }
    public string? SupplierName { get; set; }
    public int DaysInTransit { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal ActualCharges { get; set; }
    public decimal EstimatedCharges { get; set; }
    /// <summary>ثمن الشراء + المصاريف الفعلية المُضافة للتكلفة.</summary>
    public decimal CostToDate { get; set; }
    public decimal ExpectedCost { get; set; }
}

public class CarInventoryAgingRowDto
{
    public Guid Id { get; set; }
    public string ChassisNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public VehicleStatus Status { get; set; }
    public int DaysInStock { get; set; }
    /// <summary>0-30 | 31-60 | 61-90 | 91-180 | 180+</summary>
    public string AgeBucket { get; set; } = string.Empty;
    public decimal PurchasePrice { get; set; }
    public decimal AdditionalCosts { get; set; }
    public decimal PreparationCost { get; set; }
    public decimal TotalCost { get; set; }
    public decimal SellingPrice { get; set; }
}
