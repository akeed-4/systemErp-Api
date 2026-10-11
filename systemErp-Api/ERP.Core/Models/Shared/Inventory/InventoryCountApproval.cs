namespace ERP.Core.Models.Shared;

/// <summary>
/// مستند اعتماد الجرد: يُنشأ عند إرسال الجرد ويحمل قرار الاعتماد/الرفض ونتيجة التسوية (القيد وحركات المخزون).
/// لكل إرسال مستند مستقل فيبقى سجل كامل لكل الجولات. إن انطبقت سياسة اعتماد عامة يُربط بطلب <see cref="ApprovalRequest"/> متعدد المستويات.
/// </summary>
public class InventoryCountApproval : BaseEntity
{
    public string ApprovalNumber { get; set; } = string.Empty;
    public Guid InventoryCountId { get; set; }
    public string CountNumber { get; set; } = string.Empty;
    public InventoryCountScope Scope { get; set; }
    public InventoryCountApprovalStatus Status { get; set; } = InventoryCountApprovalStatus.Pending;

    public Guid RequestedByUserId { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionComment { get; set; }

    /// <summary>ملخص الفروق وقت الإرسال (لا يتغيّر بعده).</summary>
    public int VarianceLines { get; set; }
    public decimal TotalSurplusValue { get; set; }
    public decimal TotalShortageValue { get; set; }
    public decimal NetVarianceValue { get; set; }

    /// <summary>طلب الاعتماد متعدد المستويات من سياسات الموافقات (إن وُجدت سياسة لـ inventory_count).</summary>
    public Guid? ApprovalRequestId { get; set; }
    public int CurrentLevel { get; set; } = 1;
    public int TotalLevels { get; set; } = 1;

    public Guid? JournalEntryId { get; set; }
    public string? JournalEntryNumber { get; set; }
    /// <summary>رمز تزامن: يمنع اعتماد نفس المستند مرتين بالتوازي (تسوية مكررة).</summary>
    public Guid Version { get; set; } = Guid.NewGuid();
}
