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

    // ---- جدول الكميات (عقود boq_certificate) ----
    /// <summary>رقم البند في جدول الكميات (مثل 1.2.3).</summary>
    public string? LineCode { get; set; }
    /// <summary>الباب/القسم الذي يتبعه البند (أعمال ترابية، خرسانة...).</summary>
    public string? SectionName { get; set; }
    /// <summary><see cref="ContractBoqLineKinds"/>: measured | lump_sum | provisional.</summary>
    public string LineKind { get; set; } = ContractBoqLineKinds.Measured;
    /// <summary>الكمية المعتمدة بعد الملاحق؛ تساوي <see cref="Quantity"/> حتى تُعتمد ملاحق (يضبطها الخادم).</summary>
    public decimal RevisedQuantity { get; set; }
    /// <summary>نسبة السماح بتجاوز الكمية المعتمدة في المستخلصات.</summary>
    public decimal OverrunPercent { get; set; }
    public int SortOrder { get; set; }

    public virtual CommercialContract Contract { get; set; } = null!;
}