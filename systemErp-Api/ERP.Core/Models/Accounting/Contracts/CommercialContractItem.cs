namespace ERP.Core.Models.Accounting;

/// <summary>بند توريد/خدمة ضمن العقد التجاري (مرجع لبيانات التسليم والفوترة).</summary>
public class CommercialContractItem : BaseEntity
{
    public Guid ContractId { get; set; }
    public Guid? ItemId { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal VatRate { get; set; } = 15m;
    public decimal TotalWithVat { get; set; }

    public virtual CommercialContract Contract { get; set; } = null!;
}