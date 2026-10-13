namespace ERP.Core.Models.CarShowroom;

/// <summary>
/// قيد تعديل موثق على تكلفة سيارة مباعة: الطريق الوحيد لتغيير تكلفتها بعد البيع. يُسجَّل لكل مصروف فعلي وصل بعد البيع
/// (أو لتعديله)، بسببه ومن اعتمده، وأثره يُحمَّل على تكلفة المبيعات مباشرة لا على المخزون.
/// </summary>
public class VehicleCostAdjustment : BaseEntity
{
    public string AdjustmentNumber { get; set; } = string.Empty;
    public Guid VehicleId { get; set; }
    /// <summary>عقد البيع الذي بيعت به السيارة (لتقرير ربحها بعد كل المصاريف).</summary>
    public Guid? ContractId { get; set; }
    public Guid? ChargeLineId { get; set; }
    public DateTime Date { get; set; }
    /// <summary>الأثر على تكلفة السيارة بالعملة الأساسية (موجب = زيادة التكلفة).</summary>
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    /// <summary>charge_line | landed_cost_voucher | tamm</summary>
    public string Source { get; set; } = "charge_line";
    public string? ApprovedBy { get; set; }
    public Guid? JournalEntryId { get; set; }
}

public enum PrepaidExpenseStatus
{
    Active = 1,
    Completed = 2,   // أُطفئ كاملاً
    Cancelled = 3
}

/// <summary>مصروف مدفوع مقدماً (مثل اشتراك تم السنوي): يُقيَّد أصلاً عند الدفع ويُطفأ شهرياً على المصروف.</summary>
public class PrepaidExpense : BaseEntity
{
    public string Number { get; set; } = string.Empty;
    public Guid ExpenseTypeId { get; set; }
    public string ExpenseTypeNameAr { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>أول شهر يُطفأ فيه.</summary>
    public DateTime StartDate { get; set; }
    public int Months { get; set; }
    /// <summary>المبلغ قبل الضريبة بالعملة الأساسية.</summary>
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal AmortizedAmount { get; set; }
    public Guid? SupplierId { get; set; }
    public string? PaymentAccountCode { get; set; }
    public PrepaidExpenseStatus Status { get; set; } = PrepaidExpenseStatus.Active;
    public Guid? JournalEntryId { get; set; }

    public virtual ICollection<PrepaidExpenseAmortization> Amortizations { get; set; } = new List<PrepaidExpenseAmortization>();
}

/// <summary>إطفاء شهر واحد. فريد لكل (مصروف، فترة) فلا يُطفأ الشهر مرتين.</summary>
public class PrepaidExpenseAmortization : BaseEntity
{
    public Guid PrepaidExpenseId { get; set; }
    /// <summary>الفترة بصيغة yyyy-MM.</summary>
    public string Period { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public Guid? JournalEntryId { get; set; }

    public virtual PrepaidExpense PrepaidExpense { get; set; } = null!;
}
