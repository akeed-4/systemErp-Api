namespace ERP.Core.DTOs.POS;

public class CardTerminalPaymentDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Provider { get; set; } = string.Empty;
    public CardTerminalPaymentStatus Status { get; set; }
    public string Reference { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "SAR";
    public Guid ShiftId { get; set; }
    public string? TerminalName { get; set; }
    public string PaymentMethodCode { get; set; } = string.Empty;
    public string? Rrn { get; set; }
    public string? AuthCode { get; set; }
    public string? CardScheme { get; set; }
    public string? MaskedPan { get; set; }
    public string? TerminalId { get; set; }
    public string? ResponseCode { get; set; }
    public string? ResponseMessage { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool ResolvedManually { get; set; }
    public string? ResolvedBy { get; set; }
    public string? ResolutionNote { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public Guid? PosTransactionId { get; set; }
    public string? InvoiceNumber { get; set; }
}

/// <summary>بدء دفع على الجهاز: المبلغ وطريقة الدفع (المفعّل لها الجهاز). المرجع يولّده السرفر.</summary>
public class StartCardTerminalPaymentDto
{
    public decimal Amount { get; set; }
    public string PaymentMethodCode { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string? TerminalName { get; set; }
}

/// <summary>
/// نتيجة الجهاز كما نقلها برنامج الربط على جهاز الكاشير.
/// Outcome: approved | declined | unknown (انقطاع/مهلة) | not_sent (لم يصل الطلب للجهاز).
/// </summary>
public class ReportCardTerminalResultDto
{
    public string Outcome { get; set; } = string.Empty;
    public string? Rrn { get; set; }
    public string? AuthCode { get; set; }
    public string? CardScheme { get; set; }
    public string? MaskedPan { get; set; }
    public string? TerminalId { get; set; }
    public string? ResponseCode { get; set; }
    public string? ResponseMessage { get; set; }
    public string? RawResponse { get; set; }
}

/// <summary>
/// حسم إداري بعد مراجعة الجهاز أو كشف المزوّد: approved (برقم RRN ورمز الموافقة)، declined (لم يُخصم)،
/// reversed (موافقة لم تُستخدم أُلغيت/استُردّت على الجهاز). الملاحظة إلزامية.
/// </summary>
public class ResolveCardTerminalPaymentDto
{
    public string Outcome { get; set; } = string.Empty;
    public string? Rrn { get; set; }
    public string? AuthCode { get; set; }
    public string Note { get; set; } = string.Empty;
}
