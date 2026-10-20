namespace ERP.Core.DTOs.POS;

public class CreatePosReturnRequestDto
{
    public Guid OriginalTransactionId { get; set; }
    public string ReturnReason { get; set; } = string.Empty;
    public PosRefundMethod RefundMethod { get; set; } = PosRefundMethod.Cash;
    /// <summary>طريقة الرد من جدول طرق الدفع (تُقدَّم على RefundMethod): نوعها يحدّد أثر الوردية وحسابها يُرحَّل عليه الرد.</summary>
    public string? RefundPaymentMethodCode { get; set; }
    /// <summary>فارغ = إرجاع كل ما تبقّى من المعاملة.</summary>
    public List<PosReturnLineDto> Items { get; set; } = new();
}
