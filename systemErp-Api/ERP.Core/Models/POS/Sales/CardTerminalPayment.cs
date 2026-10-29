namespace ERP.Core.Models.POS;

/// <summary>
/// دفع ببطاقة عبر جهاز الدفع المربوط بنقطة البيع (مثل Geidea WebECR). يُنشأ Pending قبل الإرسال للجهاز،
/// ولا يُعدّ مدفوعاً إلا بنتيجة موافقة من الجهاز (أو حسم إداري موثَّق بعد مراجعة الجهاز). يُستهلك في عملية بيع واحدة فقط.
/// </summary>
public class CardTerminalPayment : BaseEntity
{
    /// <summary>مزوّد الربط (geidea_webecr).</summary>
    public string Provider { get; set; } = string.Empty;
    public CardTerminalPaymentStatus Status { get; set; } = CardTerminalPaymentStatus.Pending;
    /// <summary>مرجع فريد يُرسل للجهاز مع المبلغ ويُستعلم به عند انقطاع الاتصال.</summary>
    public string Reference { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "SAR";

    public Guid ShiftId { get; set; }
    public Guid CashierId { get; set; }
    public string? TerminalName { get; set; }
    /// <summary>طريقة الدفع المؤسَّسة (نوع بطاقة، مفعّل لها الجهاز) التي يُرحَّل على حسابها.</summary>
    public string PaymentMethodCode { get; set; } = string.Empty;

    // ---------- نتيجة الجهاز ----------
    public string? Rrn { get; set; }
    public string? AuthCode { get; set; }
    public string? CardScheme { get; set; }
    public string? MaskedPan { get; set; }
    public string? TerminalId { get; set; }
    public string? ResponseCode { get; set; }
    public string? ResponseMessage { get; set; }
    /// <summary>رد الجهاز كما ورد (للمراجعة والتسوية مع كشف المزوّد).</summary>
    public string? RawResponse { get; set; }
    public DateTime? CompletedAt { get; set; }

    // ---------- الحسم اليدوي ----------
    public bool ResolvedManually { get; set; }
    public string? ResolvedBy { get; set; }
    public string? ResolutionNote { get; set; }

    // ---------- الاستهلاك ----------
    public DateTime? ConsumedAt { get; set; }
    public Guid? PosTransactionId { get; set; }
    public string? InvoiceNumber { get; set; }
}
