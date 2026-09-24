namespace ERP.Core.DTOs.CarShowroom;

public partial class CarModelDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid BrandId { get; set; }
    public Guid? AgentId { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
}
