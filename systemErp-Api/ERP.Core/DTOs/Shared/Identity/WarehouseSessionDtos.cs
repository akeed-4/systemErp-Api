namespace ERP.Core.DTOs.Shared;

/// <summary>موقع (معرض/مستودع) يستطيع المستخدم العمل فيه.</summary>
public class WarehouseOptionDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

/// <summary>
/// جلسة الموقع للمستخدم الحالي: المواقع المسموحة، وهل له «كل المواقع»، والموقع الفعلي لهذا الطلب
/// (من ترويسة X-Warehouse-Id بعد التحقق)، وإعداد المنشأة لعرض المستندات.
/// </summary>
public class WarehouseSessionDto
{
    public List<WarehouseOptionDto> Warehouses { get; set; } = new();
    /// <summary>مسموح له بكل المواقع: يستطيع اختيار «كل المواقع».</summary>
    public bool AllWarehouses { get; set; }
    public Guid? CurrentWarehouseId { get; set; }
    /// <summary>site = مستندات المعرض المختار | user = مستندات المستخدم نفسه.</summary>
    public string DocumentVisibility { get; set; } = "site";
}

public class DocumentVisibilityRequestDto
{
    public string DocumentVisibility { get; set; } = "site";
}
