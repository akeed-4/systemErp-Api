namespace ERP.Core.DTOs.CarShowroom;

public partial class CreateCarColorDto
{
    public string NameAr { get; set; } = string.Empty;
    public string? Hex { get; set; }
    public bool IsExterior { get; set; } = true;
}
