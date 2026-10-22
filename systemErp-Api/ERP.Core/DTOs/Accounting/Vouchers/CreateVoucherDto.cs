namespace ERP.Core.DTOs.Accounting;

public partial class CreateVoucherDto
{
    public string? Module { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public VoucherType Type { get; set; }
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    /// <summary>ضريبة القيمة المضافة المتضمَّنة في المبلغ (لمصروف/إيراد مباشر، لا لسداد عميل أو مورد).</summary>
    public decimal VatAmount { get; set; }
    /// <summary>مركز تكلفة الحساب المقابل (مصروف أو إيراد مباشر)؛ يُحفظ مع السند وينتقل لسطر الحساب في قيده.</summary>
    public Guid? CostCenterId { get; set; }
    public string AmountInWordsAr { get; set; } = string.Empty;
    public string PartyName { get; set; } = string.Empty;
    public string PartyAccountCode { get; set; } = string.Empty;
    public string TreasuryAccountCode { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = "cash";
    /// <summary>رمز طريقة الدفع المختارة: يحدّد حساب الخزينة ونوع الدفع في السرفر (يُقدَّم على TreasuryAccountCode).</summary>
    public string? PaymentMethodCode { get; set; }
    public bool IsSplitPayment { get; set; }
    public string? ReferenceNumber { get; set; }
    public string Notes { get; set; } = string.Empty;
    public Guid? JournalEntryId { get; set; }
    public string ReceivedOrPaidBy { get; set; } = string.Empty;
    /// <summary>سند عربون: المركبة المحجوزة (قبض فقط). الحساب المقابل يُحدَّد في السرفر (عربونات العملاء).</summary>
    public Guid? DepositVehicleId { get; set; }
    public Guid? DepositCustomerId { get; set; }
    public List<VoucherPaymentSplitDto> PaymentSplits { get; set; } = new();
    /// <summary>توزيع السند على فواتير آجلة للطرف (سداد كلي أو جزئي)؛ الباقي دفعة مقدمة.</summary>
    public List<VoucherAllocationDto> Allocations { get; set; } = new();
}
