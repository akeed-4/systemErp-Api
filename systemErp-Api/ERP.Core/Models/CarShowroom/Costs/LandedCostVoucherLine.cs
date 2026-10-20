namespace ERP.Core.Models.CarShowroom;

public class LandedCostVoucherLine : BaseEntity
{
    public Guid VoucherId { get; set; }
    public int LineNo { get; set; }
    public Guid ExpenseTypeId { get; set; }
    public string ExpenseTypeCode { get; set; } = string.Empty;
    public string ExpenseTypeNameAr { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>المبلغ قبل الضريبة بعملة السند.</summary>
    public decimal Amount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal BaseAmount { get; set; }
    /// <summary>ما يُحمَّل فعلاً بالعملة الأساسية (المبلغ + الضريبة غير المستردة، وصفر لضريبة مدخلات مستردة).</summary>
    public decimal CostAmount { get; set; }
    public ChargeTreatment Treatment { get; set; }
    public ChargeAllocationMethod AllocationMethod { get; set; }

    /// <summary>
    /// تكلفة الاستيراد المقدمة التي سُحب منها هذا السطر: يُقيَّد دائناً على حساب التسوية بدل المورد، بسعر صرفها وبلا ضريبة
    /// (ضريبتها قُيِّدت عند دفعها). فارغ = سطر عادي على المورد.
    /// </summary>
    public Guid? ImportCostAdvanceId { get; set; }
    public string? ImportCostAdvanceNumber { get; set; }

    /// <summary>السطر التقديري المحدَّد يدوياً ليحل هذا السطر محله؛ فارغ = يُطابَق تلقائياً بالنوع.</summary>
    public Guid? ReplacesLineId { get; set; }
    // تُسجَّل عند الاعتماد: القيمتان والفرق
    /// <summary>السطر الذي استُبدل فعلاً، وحالته قبل الاستبدال (لإعادته عند إلغاء السند).</summary>
    public Guid? ReplacedLineId { get; set; }
    public ChargeStatus? ReplacedLineStatus { get; set; }
    public decimal? EstimatedAmount { get; set; }
    /// <summary>الفعلي − التقديري (موجب = زيادة على المتوقع).</summary>
    public decimal? Variance { get; set; }
    /// <summary>سطر المصروف الفعلي المُنشأ عند الاعتماد.</summary>
    public Guid? ChargeLineId { get; set; }

    public virtual LandedCostVoucher Voucher { get; set; } = null!;
    public virtual ICollection<LandedCostVoucherAllocation> Allocations { get; set; } = new List<LandedCostVoucherAllocation>();
}

/// <summary>حصة سيارة من سطر السند بالعملة الأساسية، كما عُرضت في معاينة التوزيع.</summary>
public class LandedCostVoucherAllocation : BaseEntity
{
    public Guid LineId { get; set; }
    public Guid VehicleId { get; set; }
    public decimal Amount { get; set; }

    public virtual LandedCostVoucherLine Line { get; set; } = null!;
}
