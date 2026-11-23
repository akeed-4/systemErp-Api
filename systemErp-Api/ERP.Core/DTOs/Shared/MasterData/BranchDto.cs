namespace ERP.Core.DTOs.Shared;

public partial class BranchDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? Address { get; set; }
    public string? ManagerName { get; set; }
    public string? Phone { get; set; }
    public bool IsDefault { get; set; }
    public string Status { get; set; } = "active";
}

public partial class CreateBranchDto
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? Address { get; set; }
    public string? ManagerName { get; set; }
    public string? Phone { get; set; }
    public bool IsDefault { get; set; }
    public string Status { get; set; } = "active";
}

public partial class UpdateBranchDto : CreateBranchDto
{
}
