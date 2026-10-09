namespace ERP.Core.DTOs.Accounting;

/// <summary>سطر سيارة في فاتورة. الحقول المحسوبة (الضريبة والإجماليات والتكلفة) تُتجاهل من العميل وتُحسب في السرفر.</summary>
public class InvoiceVehicleLineDto
{
    public Guid Id { get; set; }
    public int LineNo { get; set; }
    public Guid? VehicleId { get; set; }
    public string? Vin { get; set; }
    public string? TempRef { get; set; }
    public Guid? BrandId { get; set; }
    public Guid? ModelId { get; set; }
    public Guid? TrimId { get; set; }
    public string BrandNameAr { get; set; } = string.Empty;
    public string ModelNameAr { get; set; } = string.Empty;
    public string? TrimNameAr { get; set; }
    public int Year { get; set; }
    public string ColorExterior { get; set; } = string.Empty;
    public string ColorInterior { get; set; } = string.Empty;
    public string? EngineNumber { get; set; }
    public string? CustomsCardNumber { get; set; }
    public VatMode VatMode { get; set; } = VatMode.Standard_15;
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalBeforeVat { get; set; }
    public decimal TotalAfterVat { get; set; }
    public decimal UnitCost { get; set; }
}
