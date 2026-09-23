using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class CarModel : BaseEntity
{
    public Guid BrandId { get; set; }
    public Guid? AgentId { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
}
