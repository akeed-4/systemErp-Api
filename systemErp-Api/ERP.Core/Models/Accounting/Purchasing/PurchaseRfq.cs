namespace ERP.Core.Models.Accounting;

/// <summary>
/// طلب عرض أسعار (RFQ) من طلب شراء معتمد بلا أسعار إلى عدة موردين. عروض الموردين عروض أسعار شراء
/// (<see cref="Quotation"/> بنوع purchase_quotation و<see cref="Quotation.RfqId"/>)، والترسية تنشئ أوامر شراء
/// (<see cref="CommercialOrder"/> بنوع purchase_order) يُستلم عليها بسندات استلام جزئية وتُفوتر كل سند بفاتورة مورد.
/// </summary>
public class PurchaseRfq : BaseEntity
{
    public string RfqNumber { get; set; } = string.Empty;
    public Guid RequisitionId { get; set; }
    /// <summary>مشروع الطلب (للعرض والفلترة)؛ من الطلب نفسه.</summary>
    public Guid? ContractId { get; set; }
    public Guid? WarehouseId { get; set; }
    public DateTime Date { get; set; }
    public DateTime? DueDate { get; set; }
    /// <summary>open | awarded | cancelled</summary>
    public string Status { get; set; } = PurchaseRfqStatuses.Open;
    public string? Notes { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string? AwardedBy { get; set; }
    public DateTime? AwardedAt { get; set; }

    public virtual ICollection<PurchaseRfqItem> Items { get; set; } = new List<PurchaseRfqItem>();
    public virtual ICollection<PurchaseRfqSupplier> Suppliers { get; set; } = new List<PurchaseRfqSupplier>();
}

public class PurchaseRfqItem : BaseEntity
{
    public Guid RfqId { get; set; }
    public Guid? RequisitionItemId { get; set; }
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }

    public virtual PurchaseRfq Rfq { get; set; } = null!;
}

public class PurchaseRfqSupplier : BaseEntity
{
    public Guid RfqId { get; set; }
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;

    public virtual PurchaseRfq Rfq { get; set; } = null!;
}

public static class PurchaseRfqStatuses
{
    public const string Open = "open";
    public const string Awarded = "awarded";
    public const string Cancelled = "cancelled";
}
