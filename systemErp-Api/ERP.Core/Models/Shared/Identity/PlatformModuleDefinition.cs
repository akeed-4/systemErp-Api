namespace ERP.Core.Models.Shared;

/// <summary>بيانات تعريف وحدة منتج في المنصة؛ لا تتبع منشأة ويمكن قراءتها من كل العملاء المسجلين.</summary>
public class PlatformModuleDefinition
{
    public int Number { get; set; }
    public string Key { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string DescriptionAr { get; set; } = string.Empty;
    public string DescriptionEn { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}