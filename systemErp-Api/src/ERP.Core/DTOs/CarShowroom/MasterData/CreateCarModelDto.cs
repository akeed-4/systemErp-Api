namespace ERP.Core.DTOs.CarShowroom;

public partial class CreateCarModelDto
{
    public Guid BrandId { get; set; }
    public Guid? AgentId { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
}
