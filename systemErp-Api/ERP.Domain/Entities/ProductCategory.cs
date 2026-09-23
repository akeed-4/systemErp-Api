using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class ProductCategory : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public string? Description { get; set; }
}
