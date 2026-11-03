namespace ERP.Core.Models.POS;

public enum LoyaltyPointEntryStatus
{
    /// <summary>مكتسبة وتنتظر انتهاء مدة الترجيع: لا تُستبدل.</summary>
    Pending = 1,
    /// <summary>انتقلت لرصيد العميل المتاح.</summary>
    Released = 2,
    /// <summary>أُلغيت بمرتجع كامل أو إلغاء الفاتورة قبل إتاحتها.</summary>
    Cancelled = 3,
}

/// <summary>
/// نقاط فاتورة معلّقة حتى تنتهي مدة الترجيع: تُضاف لرصيد العميل المتاح عند حلول موعدها، والمرتجع قبله يخصم منها أولاً
/// (فلا يستبدل العميل نقاط فاتورة ثم يرجعها).
/// </summary>
public class LoyaltyPointEntry : BaseEntity
{
    public Guid CustomerId { get; set; }
    public Guid TransactionId { get; set; }
    /// <summary>النقاط المتبقية في القيد (تنقص بالمرتجع الجزئي قبل الإتاحة).</summary>
    public int Points { get; set; }
    public int OriginalPoints { get; set; }
    /// <summary>موعد الإتاحة (UTC).</summary>
    public DateTime AvailableAt { get; set; }
    public LoyaltyPointEntryStatus Status { get; set; } = LoyaltyPointEntryStatus.Pending;
    /// <summary>تزامن: عمليتان على القيد نفسه (إتاحة/سحب) لا تنجحان معاً؛ الثانية تُعاد على الحالة الجديدة.</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
