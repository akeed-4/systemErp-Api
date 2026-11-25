namespace ERP.Core.Models.Accounting;

public enum DeliveryNoteStatus
{
    Draft = 1,
    Delivered = 2,
    PartiallyReturned = 3,
    Returned = 4,
    Invoiced = 5,
    /// <summary>ملغى بسبب (بلا تسليم فعلي ولا فوترة): لا يُحذف، ويحرر كمياته المحجوزة.</summary>
    Cancelled = 6,
    /// <summary>فُوتر جزء من الكميات المؤكد تسليمها (فواتير مرحلية متعددة على البيان).</summary>
    PartiallyInvoiced = 7
}
