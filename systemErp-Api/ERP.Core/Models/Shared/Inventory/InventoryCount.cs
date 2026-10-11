namespace ERP.Core.Models.Shared;

/// <summary>
/// مستند الجرد الفعلي: يسجّل الكميات المعدودة مقابل رصيد النظام لأصناف المخزون أو مركبات المعرض.
/// رصيد النظام يُلتقط عند الحفظ ويُجمَّد عند الإرسال للاعتماد؛ التسوية لا تتم إلا باعتماد <see cref="InventoryCountApproval"/>.
/// </summary>
public class InventoryCount : BaseEntity
{
    public string CountNumber { get; set; } = string.Empty;
    public InventoryCountScope Scope { get; set; } = InventoryCountScope.Items;
    public InventoryCountType CountType { get; set; } = InventoryCountType.Full;
    public InventoryCountStatus Status { get; set; } = InventoryCountStatus.Draft;
    public DateTime CountDate { get; set; }

    /// <summary>مستودع الجرد (للأصناف): يُسجَّل على حركات التسوية. الرصيد نفسه على مستوى الصنف.</summary>
    public Guid? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    /// <summary>موقع المعرض (للمركبات): يحدّد المركبات المتوقعة في هذا الجرد.</summary>
    public string? Location { get; set; }

    public string CountedBy { get; set; } = string.Empty;
    public Guid? CreatedByUserId { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public string? RejectionReason { get; set; }

    public int TotalLines { get; set; }
    public int CountedLines { get; set; }
    public int VarianceLines { get; set; }
    public decimal TotalSurplusValue { get; set; }
    public decimal TotalShortageValue { get; set; }
    public decimal NetVarianceValue { get; set; }

    public Guid? JournalEntryId { get; set; }
    public string? JournalEntryNumber { get; set; }
    public string? Notes { get; set; }

    public virtual ICollection<InventoryCountLine> Lines { get; set; } = new List<InventoryCountLine>();
}
