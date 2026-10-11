namespace ERP.Core.Models.POS;

/// <summary>مرتجع بيع نقطة البيع. الأثر المالي والمخزني في الإشعار الدائن المرتبط (CreditNoteInvoiceId).</summary>
public class PosSalesReturn : BaseEntity
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
    /// <summary>completed | cancelled</summary>
    public string Status { get; set; } = "completed";
    public Guid? CreditNoteInvoiceId { get; set; }
    /// <summary>النقاط التي سُحبت من العميل عند هذا المرتجع (تُعاد عند حذفه).</summary>
    public int PointsRevoked { get; set; }

    public virtual ICollection<PosSalesReturnItem> Items { get; set; } = new List<PosSalesReturnItem>();
}
