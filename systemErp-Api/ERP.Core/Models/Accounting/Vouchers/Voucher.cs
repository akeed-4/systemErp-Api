
namespace ERP.Core.Models.Accounting;

public class Voucher : BaseEntity, IScopedRecord
{
    /// <summary>منشئ المستند (يُختم آلياً) لنطاق «السجلات التي أنشأها».</summary>
    public Guid? CreatedByUserId { get; set; }
    /// <summary>المستخدم المسند إليه المستند لنطاق «السجلات المسندة إليه».</summary>
    public Guid? AssignedToUserId { get; set; }
    public string Module { get; set; } = "accounting";
    public string VoucherNumber { get; set; } = string.Empty;
    public VoucherType Type { get; set; }
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    /// <summary>ضريبة القيمة المضافة المتضمَّنة في المبلغ (مصروف أو إيراد مباشر بضريبة)؛ 0 لسداد الذمم.</summary>
    public decimal VatAmount { get; set; }
    /// <summary>مركز تكلفة الحساب المقابل (مصروف أو إيراد مباشر)؛ يُحفظ مع السند وينتقل لسطر الحساب في قيده.</summary>
    public Guid? CostCenterId { get; set; }
    public string AmountInWordsAr { get; set; } = string.Empty;
    
    public string PartyName { get; set; } = string.Empty;
    public string PartyAccountCode { get; set; } = string.Empty;
    public string TreasuryAccountCode { get; set; } = string.Empty;
    
    public string PaymentMethod { get; set; } = "cash";
    /// <summary>رمز طريقة الدفع المختارة (جدول طرق الدفع)؛ حساب الخزينة ونوع الدفع منها. فارغ = سند قديم بحسابه المرسل.</summary>
    public string? PaymentMethodCode { get; set; }
    public bool IsSplitPayment { get; set; }
    public string? ReferenceNumber { get; set; }
    public string Notes { get; set; } = string.Empty;
    
    public Guid? JournalEntryId { get; set; }
    public string ReceivedOrPaidBy { get; set; } = string.Empty;

    // سند عربون سيارة: قبض على المركبة يُحجز به، ويُقفل عند تطبيقه على فاتورة بيعها.
    /// <summary>المركبة المحجوزة بالعربون؛ null = سند عادي.</summary>
    public Guid? DepositVehicleId { get; set; }
    public Guid? DepositCustomerId { get; set; }
    /// <summary>open = مقبوض لم يُطبَّق | applied = خُصم من فاتورة بيع. null لغير العربون.</summary>
    public string? DepositStatus { get; set; }
    public Guid? DepositContractId { get; set; }

    public virtual ICollection<VoucherPaymentSplit> PaymentSplits { get; set; } = new List<VoucherPaymentSplit>();
    /// <summary>توزيع السند على فواتير آجلة؛ ما لم يُوزَّع دفعة مقدمة على حساب الطرف.</summary>
    public virtual ICollection<VoucherAllocation> Allocations { get; set; } = new List<VoucherAllocation>();
}
