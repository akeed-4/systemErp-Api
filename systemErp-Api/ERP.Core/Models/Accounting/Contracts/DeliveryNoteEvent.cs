namespace ERP.Core.Models.Accounting;

/// <summary>سجل أحداث بيان التسليم: الإنشاء والخروج والتأكيد والإلغاء والفوترة والمرتجع. لا يُعدَّل ولا يُحذف.</summary>
public class DeliveryNoteEvent : BaseEntity
{
    public Guid DeliveryNoteId { get; set; }
    /// <summary>created | updated | dispatched | delivered | partially_delivered | cancelled | invoiced | invoice_released | returned</summary>
    public string EventType { get; set; } = string.Empty;
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public string? Notes { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public DateTime OccurredAt { get; set; }
}

/// <summary>
/// فوترة مرحلية لسطر بيان اتفاقية متتبَّع: كم فُوتر منه في كل فاتورة. يمنع فوترة الكمية نفسها مرتين، وحذف الفاتورة يحرر كمياتها.
/// </summary>
public class DeliveryNoteInvoiceLine : BaseEntity
{
    public Guid DeliveryNoteId { get; set; }
    public Guid DeliveryNoteItemId { get; set; }
    public Guid InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
}
