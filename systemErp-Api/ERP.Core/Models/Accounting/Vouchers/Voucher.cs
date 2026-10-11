
namespace ERP.Core.Models.Accounting;

public class Voucher : BaseEntity
{
    public string VoucherNumber { get; set; } = string.Empty;
    public VoucherType Type { get; set; }
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    /// <summary>ضريبة القيمة المضافة المتضمَّنة في المبلغ (مصروف أو إيراد مباشر بضريبة)؛ 0 لسداد الذمم.</summary>
    public decimal VatAmount { get; set; }
    public string AmountInWordsAr { get; set; } = string.Empty;
    
    public string PartyName { get; set; } = string.Empty;
    public string PartyAccountCode { get; set; } = string.Empty;
    public string TreasuryAccountCode { get; set; } = string.Empty;
    
    public string PaymentMethod { get; set; } = "cash";
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
