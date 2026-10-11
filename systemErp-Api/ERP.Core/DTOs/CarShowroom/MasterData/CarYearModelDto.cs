namespace ERP.Core.DTOs.CarShowroom;

public partial class CarYearModelDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid TrimId { get; set; }
    public int Year { get; set; }
}
