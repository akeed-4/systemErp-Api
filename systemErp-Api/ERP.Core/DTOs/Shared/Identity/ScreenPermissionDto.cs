namespace ERP.Core.DTOs.Shared;

/// <summary>يطابق ScreenPermission في erp.models.ts.</summary>
public class ScreenPermissionDto
{
    public string ScreenId { get; set; } = string.Empty;
    public string ScreenNameAr { get; set; } = string.Empty;
    public string ScreenNameEn { get; set; } = string.Empty;
    public bool CanView { get; set; }
    public bool CanCreate { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
    public bool CanApprove { get; set; }
    /// <summary>
    /// كل الإجراءات الممنوحة برموزها (view, create, approve, post, print...). عند القراءة تُملأ دائماً (وتُشتق للسجلات القديمة)؛
    /// وعند الحفظ إن أُرسلت تُعتمد هي وتُضبط الأعمدة الخمسة منها، وإلا يبقى الحفظ بالأعمدة الخمسة كما كان (عملاء قدامى).
    /// </summary>
    public List<string>? Actions { get; set; }
}
