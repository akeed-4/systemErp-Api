namespace ERP.Core.DTOs.Accounting;

public partial class ContractItemDto
{
    public Guid Id { get; set; }
    public Guid? ItemId { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal VatRate { get; set; } = 15m;
    public decimal TotalWithVat { get; set; }
    public string? LineCode { get; set; }
    public string? SectionName { get; set; }
    public string LineKind { get; set; } = "measured";
    /// <summary>للقراءة: يضبطها الخادم (= الكمية حتى تُعتمد ملاحق).</summary>
    public decimal RevisedQuantity { get; set; }
    public decimal OverrunPercent { get; set; }
    public int SortOrder { get; set; }
}