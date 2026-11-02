namespace ERP.Core.DTOs.Shared;

/// <summary>طلب رقم مستند من خدمة الترقيم.</summary>
public sealed class DocumentNumberRequest
{
    public required string Key { get; init; }
    public required string DefaultPrefix { get; init; }
    /// <summary>مستودع المستند نفسه (لنطاق الترقيم لكل مستودع)؛ فارغ = مستودع الجلسة.</summary>
    public Guid? WarehouseId { get; init; }
    /// <summary>تاريخ المستند (لتحديد السنة المالية)؛ فارغ = اليوم.</summary>
    public DateTime? Date { get; init; }
    /// <summary>الرقم الذي أدخله المستخدم (يُعتمد فقط إن كان نوع المستند يدوياً).</summary>
    public string? ManualNumber { get; init; }
    /// <summary>
    /// المستند من شاشة المستخدم فيخضع للترقيم اليدوي إن ضُبط (الرقم إلزامي حينها). مستندات النظام
    /// (نقاط البيع، القيود الآلية، التحويل من مستند آخر) تبقى تلقائية دائماً.
    /// </summary>
    public bool UserEntered { get; init; }
    /// <summary>هل الرقم مستخدم في جدول المستند؟ يُرفض اليدوي المكرر، ويتجاوز التلقائي رقماً سبق إدخاله يدوياً.</summary>
    public Func<string, CancellationToken, Task<bool>>? Exists { get; init; }
}

/// <summary>العدّاد الفعلي لمستند: مفتاحه وبادئته وطوله وقيمته الحالية والرقم التالي المتوقع.</summary>
public sealed class DocumentCounterInfo
{
    public string CounterKey { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
    public int Padding { get; set; }
    public long LastValue { get; set; }
    public long NextNumber => LastValue + 1;
    public string NextFormatted { get; set; } = string.Empty;
    public int? FiscalYear { get; set; }
    public bool Manual { get; set; }
}
