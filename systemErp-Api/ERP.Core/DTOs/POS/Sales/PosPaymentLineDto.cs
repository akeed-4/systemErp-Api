namespace ERP.Core.DTOs.POS;

/// <summary>سطر دفع في نقطة البيع بطريقة من جدول طرق الدفع. الاسم والنوع يملؤهما السرفر عند القراءة (للإيصال والسجل).</summary>
public class PosPaymentLineDto
{
    public string PaymentMethodCode { get; set; } = string.Empty;
    /// <summary>المبلغ المستلم بهذه الطريقة (النقد قد يزيد عن المطلوب ويُرد الباقي منه).</summary>
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
    public string? NameAr { get; set; }
    public string? NameEn { get; set; }
    /// <summary>cash | card | bank | cheque | credit</summary>
    public string? Type { get; set; }
}
