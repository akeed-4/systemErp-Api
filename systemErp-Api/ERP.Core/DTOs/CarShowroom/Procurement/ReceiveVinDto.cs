namespace ERP.Core.DTOs.CarShowroom;

public class ReceiveVinDto
{
    public string Vin { get; set; } = string.Empty;
    public string? EngineNumber { get; set; }
    public string? CustomsCardNumber { get; set; }
    /// <summary>بند الأمر الذي تتبعه المركبة (اختياري: يُستنتج من الماركة/الموديل/السنة).</summary>
    public Guid? ItemId { get; set; }
    public string? ColorExterior { get; set; }
    public string? ColorInterior { get; set; }
    public decimal? SellingPrice { get; set; }
}
