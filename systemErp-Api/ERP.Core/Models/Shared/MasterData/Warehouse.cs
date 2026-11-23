
namespace ERP.Core.Models.Shared;

/// <summary>
/// الموقع التشغيلي (المعرض/المحل/موقع العمل)، ويظهر في الواجهة باسم «موقع». يتبع فرعاً واحداً (<see cref="BranchId"/>)،
/// وله حساباته المحاسبية الخاصة وأرصدة مخزونه. (اسم الصنف Warehouse تاريخي ويُبقى عليه لتقليل التعديلات.)
/// </summary>
public class Warehouse : BaseEntity
{
    /// <summary>الفرع الذي يتبعه الموقع (من النشاط نفسه). نقل الموقع بين الفروع لا يغيّر حساباته ولا قيوده.</summary>
    public Guid BranchId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string? ManagerName { get; set; }
    public string? Phone { get; set; }
    public bool IsDefault { get; set; }
    public string Status { get; set; } = "active"; // active | inactive
    /// <summary>حساب مخزون الموقع؛ فارغ = حساب المخزون في إعدادات الحسابات. الحساب اليدوي في المستند يتقدم عليه.</summary>
    public string? InventoryAccountCode { get; set; }
    /// <summary>
    /// حسابات الموقع الخاصة (تُنشأ تلقائياً للموقع الإضافي باسمه): مبيعاته تحت المبيعات، وتكلفة إيراده تحت تكلفة المبيعات، وصندوقه تحت
    /// النقدية. فارغ = حساب الإعدادات (الموقع الرئيسي). الحساب المحدد في المستند أو سطره يتقدم عليها.
    /// </summary>
    public string? SalesAccountCode { get; set; }
    public string? CogsAccountCode { get; set; }
    public string? CashAccountCode { get; set; }
    /// <summary>عجز وزيادة مخزون الموقع (فروق الجرد المستمر): تُنشأ لكل موقع حتى الرئيسي تحت مجموعة حسابي الإعدادات. فارغ = حساب الإعدادات.</summary>
    public string? ShortageAccountCode { get; set; }
    public string? SurplusAccountCode { get; set; }
}
