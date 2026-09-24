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
}
