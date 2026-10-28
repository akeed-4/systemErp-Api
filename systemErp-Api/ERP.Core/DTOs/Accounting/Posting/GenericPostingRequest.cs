using ERP.Core.Models.Shared;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

/// <summary>قيد عام متوازن (يدوي أو من أي وحدة).</summary>
public class GenericPostingRequest
{
    public string? Module { get; set; }
    /// <summary>تعديل مستند مرحّل: يُستبدل هذا القيد في مكانه (نفس رقمه) بدل إنشاء قيد جديد.</summary>
    public Guid? ReplaceEntryId { get; set; }
    public DateTime Date { get; set; } = SaudiTime.Today;
    public string Description { get; set; } = string.Empty;
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public string? SourceNumber { get; set; }
    public List<PostingLine> Lines { get; set; } = new();
    /// <summary>
    /// مستند يردّ مستنداً سابقاً (مرتجع): مراكز أسطره من الأصل، فيُقبل المركز ولو أُوقف بعد الأصل حتى يُلغى الأثر في مركزه نفسه.
    /// وجود المركز في المنشأة وإعداد الحساب يبقيان شرطاً.
    /// </summary>
    public bool AllowInactiveCostCenters { get; set; }
    /// <summary>
    /// مركز التكلفة الذي اختاره المستخدم لحساب بعينه في قيد المستند (من قسم القيد في شاشة الفاتورة): يحلّ محل مركز أسطر ذلك
    /// الحساب قبل قواعد المراكز، فيبقى الحساب «غير المحلَّل» بلا مركز والمركز الموقوف مرفوضاً. لا يغيّر المبالغ.
    /// </summary>
    public Dictionary<string, Guid> CostCenterByAccount { get; set; } = new();
}
