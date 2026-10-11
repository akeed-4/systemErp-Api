namespace ERP.Core.DTOs.Shared;

public partial class ScreenPermissionItemDto
{
    public Guid Id { get; set; }
    public string ScreenId { get; set; } = string.Empty;
    public bool CanView { get; set; }
    public bool CanCreate { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
    public bool CanApprove { get; set; }
}
