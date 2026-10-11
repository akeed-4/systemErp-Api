namespace ERP.Core.DTOs.POS;

public partial class CreatePosSalesReturnDto
{
    public string ReturnNumber { get; set; } = string.Empty;
    public Guid OriginalTransactionId { get; set; }
    public string OriginalInvoiceNumber { get; set; } = string.Empty;
    public Guid? OriginalInvoiceId { get; set; }
    public Guid? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public Guid ShiftId { get; set; }
    public string ReturnReason { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal VatAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public PosRefundMethod RefundMethod { get; set; } = PosRefundMethod.Cash;
    public string Status { get; set; } = "completed";
    public Guid? CreditNoteInvoiceId { get; set; }
    public List<PosSalesReturnItemDto> Items { get; set; } = new();
}
