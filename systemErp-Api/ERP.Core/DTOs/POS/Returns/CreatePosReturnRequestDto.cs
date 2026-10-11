namespace ERP.Core.DTOs.POS;

public class CreatePosReturnRequestDto
{
    public Guid OriginalTransactionId { get; set; }
    public string ReturnReason { get; set; } = string.Empty;
    public PosRefundMethod RefundMethod { get; set; } = PosRefundMethod.Cash;
    /// <summary>فارغ = إرجاع كل ما تبقّى من المعاملة.</summary>
    public List<PosReturnLineDto> Items { get; set; } = new();
}
