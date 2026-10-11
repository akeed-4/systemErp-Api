namespace ERP.Core.DTOs.Shared;

public partial class CreateProductDto
{
    public string Sku { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal CurrentStock { get; set; }
    public decimal AverageCost { get; set; }
    public decimal LastPurchaseCost { get; set; }
    public decimal? StandardCost { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal VatRate { get; set; } = 15m;
    /// <summary>التصنيف الضريبي للصنف: Standard | ZeroRated | Exempt | OutOfScope.</summary>
    public VatCategory VatCategory { get; set; } = VatCategory.Standard;
    public string? VatExemptionReasonCode { get; set; }
    public decimal MinStockLevel { get; set; }
    public string? Notes { get; set; }
}
