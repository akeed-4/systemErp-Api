namespace ERP.Core.DTOs.POS;

/// <summary>طلب إتمام بيع. الأسعار والضريبة والعروض تُحسب في السرفر من الكتالوج، لا من العميل.</summary>
public class CheckoutRequestDto
{
    public Guid? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerTaxNumber { get; set; }
    /// <summary>simplified | standard (فارغ = الافتراضي من الإعدادات)</summary>
    public string? InvoiceType { get; set; }
    public List<CheckoutLineDto> Items { get; set; } = new();

    public string? CouponCode { get; set; }
    public int LoyaltyPointsToRedeem { get; set; }

    public PosPaymentMethod PaymentMethod { get; set; } = PosPaymentMethod.Cash;
    /// <summary>المبالغ المدفوعة (النقد قد يزيد عن المطلوب ويُحتسب الباقي).</summary>
    public decimal PaidCash { get; set; }
    public decimal PaidCard { get; set; }
    public decimal PaidMada { get; set; }
    public decimal PaidApplePay { get; set; }
    /// <summary>دفع إلكتروني مكتمل عبر Paymob (بطاقة/مدى/Apple Pay) يُستهلك في هذه العملية.</summary>
    public Guid? OnlinePaymentId { get; set; }
    /// <summary>دفع مُوافَق عليه من جهاز الدفع (سطر الدفع الوحيد بطريقته وكامل الإجمالي) يُستهلك في هذه العملية.</summary>
    public Guid? CardTerminalPaymentId { get; set; }
    /// <summary>
    /// الدفع بطرق جدول طرق الدفع (تُقدَّم على PaymentMethod و Paid*): كل سطر يُرحَّل على حساب طريقته،
    /// والنقد يدخل درج الوردية والبطاقة/البنك في مبيعات البطاقات. الآجل لا يُخلط بغيره.
    /// </summary>
    public List<PosPaymentLineDto> Payments { get; set; } = new();
    /// <summary>معرّف فريد يولّده الجهاز: إعادة إرسال العملية نفسها (بعد انقطاع أو مهلة) تُرجعها ولا تكررها.</summary>
    public string? ClientReference { get; set; }
    /// <summary>بيع تم أوف لاين ويُرحَّل الآن (نقد فقط، خلال 12 ساعة من وقت البيع).</summary>
    public OfflineSaleInfoDto? Offline { get; set; }
}

/// <summary>بيانات بيع أوف لاين كما تمت على الجهاز.</summary>
public class OfflineSaleInfoDto
{
    /// <summary>وقت البيع على الجهاز (UTC).</summary>
    public DateTime IssuedAt { get; set; }
    /// <summary>رقم الإيصال المطبوع للعميل.</summary>
    public string ReceiptNumber { get; set; } = string.Empty;
    /// <summary>الإجمالي المطبوع على الإيصال (للمطابقة).</summary>
    public decimal PrintedTotal { get; set; }
    /// <summary>الوردية التي بيع فيها: يجب أن تكون وردية الكاشير المفتوحة نفسها.</summary>
    public Guid ShiftId { get; set; }
}
