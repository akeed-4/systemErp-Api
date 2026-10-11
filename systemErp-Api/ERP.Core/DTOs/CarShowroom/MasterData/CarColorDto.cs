namespace ERP.Core.DTOs.CarShowroom;

public partial class CarColorDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? Hex { get; set; }
    public bool IsExterior { get; set; } = true;
}
