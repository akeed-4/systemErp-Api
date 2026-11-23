namespace ERP.Core.DTOs.Shared;

public partial class CreateWarehouseDto
{
    /// <summary>
    /// الفرع الذي يتبعه الموقع (من النشاط نفسه وفعّال). عند الإنشاء: إلزامي إن كان للنشاط أكثر من فرع، ويُلحق بالفرع الوحيد إن لم يُرسَل.
    /// عند التعديل: غير المرسَل يبقى، والمرسَل المختلف ينقل الموقع إلى فرع آخر (دون تغيير حساباته أو قيوده).
    /// </summary>
    public Guid? BranchId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string? ManagerName { get; set; }
    public string? Phone { get; set; }
    public bool IsDefault { get; set; }
    public string Status { get; set; } = "active";
    /// <summary>
    /// حساب مخزون الموقع. عند الإنشاء: فارغ = يُنشأ للموقع حسابه تحت 114 تلقائياً. عند التعديل: غير مرسَل (null) = يبقى كما هو،
    /// "" = يعود لحساب المخزون في الإعدادات.
    /// </summary>
    public string? InventoryAccountCode { get; set; }
}
