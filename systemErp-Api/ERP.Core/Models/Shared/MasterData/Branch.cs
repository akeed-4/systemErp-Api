namespace ERP.Core.Models.Shared;

/// <summary>
/// الفرع: وحدة إدارية تابعة للنشاط (المنشأة) تجمع مواقع تشغيلية (<see cref="Warehouse"/>). الهيكل ثابت بثلاثة مستويات:
/// النشاط ← الفرع ← الموقع. الفرع بلا حسابات ولا مخزون: الحسابات والأرصدة للموقع، والمستندات تحمل موقعها وفرعها يُستنتج منه.
/// </summary>
public class Branch : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? Address { get; set; }
    public string? ManagerName { get; set; }
    public string? Phone { get; set; }
    /// <summary>الفرع الافتراضي للنشاط: تُلحق به المواقع حين لا يُحدَّد فرع ولا يوجد غيره.</summary>
    public bool IsDefault { get; set; }
    public string Status { get; set; } = "active"; // active | inactive
}
