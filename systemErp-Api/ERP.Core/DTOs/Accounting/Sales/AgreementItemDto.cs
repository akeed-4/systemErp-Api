namespace ERP.Core.DTOs.Accounting;

public partial class AgreementItemDto
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string? ItemCode { get; set; }
    public string? Barcode { get; set; }
    public string? Unit { get; set; }
    /// <summary>الكمية المستهلكة بأوامر معتمدة (يديرها السرفر).</summary>
    public decimal UsedQuantity { get; set; }
    public decimal Discount { get; set; }
    public decimal VatRate { get; set; } = 15m;
    public decimal MinQuantity { get; set; }
    /// <summary>0 = بلا حد أعلى للأمر الواحد.</summary>
    public decimal MaxQuantity { get; set; }
    public string Status { get; set; } = "active";
    public string? Notes { get; set; }
}
