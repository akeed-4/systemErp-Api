namespace ERP.Core.Models.POS;

/// <summary>
/// عملية بيع في نقطة البيع. الأثر المالي والمخزني تحمله الفاتورة المرتبطة (InvoiceId) فقط؛ هذا الكيان
/// يحفظ ما هو خاص بالكاشير: الوردية، الكوبون والولاء، توزيع المدفوعات والباقي.
/// </summary>
public class PosTransaction : BaseEntity
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public Guid ShiftId { get; set; }
    public Guid? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public string? CustomerTaxNumber { get; set; }
    /// <summary>simplified | standard</summary>
    public string InvoiceType { get; set; } = "simplified";

    public decimal SubtotalBeforeVat { get; set; }
    public decimal CouponDiscount { get; set; }
    public string? CouponCode { get; set; }
    public decimal LoyaltyDiscount { get; set; }
    public int LoyaltyPointsRedeemed { get; set; }
    public decimal TotalDiscount { get; set; }
    public decimal ExciseTaxAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal GrandTotal { get; set; }

    public PosPaymentMethod PaymentMethod { get; set; } = PosPaymentMethod.Cash;
    public decimal PaidCash { get; set; }
    public decimal PaidCard { get; set; }
    public decimal PaidMada { get; set; }
    public decimal PaidApplePay { get; set; }
    public decimal ChangeAmount { get; set; }
    /// <summary>طرق الدفع المختارة من جدول طرق الدفع (JSON لقائمة PosPaymentLineDto)؛ فارغ = عملية بالأنواع الثابتة.</summary>
    public string? PaymentsJson { get; set; }
    public int PointsEarned { get; set; }

    public string QrCodeBase64 { get; set; } = string.Empty;
    public ZatcaSubmissionStatus ZatcaStatus { get; set; } = ZatcaSubmissionStatus.NotSubmitted;
    public string? ZatcaUuid { get; set; }
    public string? ZatcaResponseMsg { get; set; }
    public Guid? InvoiceId { get; set; }
    public PosTransactionStatus Status { get; set; } = PosTransactionStatus.Completed;

    // ---- البيع أوف لاين (يُرحَّل عند عودة الاتصال) ----
    /// <summary>معرّف يولّده الجهاز لكل عملية بيع: إعادة الإرسال بعد انقطاع تُرجع العملية نفسها ولا تكررها.</summary>
    public string? ClientReference { get; set; }
    public bool IsOffline { get; set; }
    /// <summary>وقت البيع الفعلي على الجهاز (UTC)؛ الفاتورة تُؤرَّخ به لا بوقت المزامنة.</summary>
    public DateTime? OfflineIssuedAt { get; set; }
    /// <summary>رقم الإيصال المطبوع أوف لاين (من الجهاز) قبل أن يمنح السرفر رقم الفاتورة.</summary>
    public string? OfflineReceiptNumber { get; set; }
    public decimal? OfflinePrintedTotal { get; set; }
    /// <summary>ملاحظات مراجعة المزامنة: سعر تغيّر منذ البيع، فرق إجمالي، أو مخزون صار سالباً.</summary>
    public string? OfflineReviewNote { get; set; }

    public virtual ICollection<PosTransactionItem> Items { get; set; } = new List<PosTransactionItem>();
}
