using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

public class PurchasePostingRequest
{
    public DateTime Date { get; set; } = DateTime.UtcNow;
    public string Description { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceId { get; set; }
    public string SourceNumber { get; set; } = string.Empty;
    public bool IsReturn { get; set; }

    /// <summary>حساب المورد (ذمم دائنة). فارغ = الحساب الافتراضي 211.</summary>
    public string? PartyAccountCode { get; set; }
    /// <summary>حساب المخزون/المشتريات المدين. فارغ = مخزون البضاعة 1141.</summary>
    public string? InventoryAccountCode { get; set; }

    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public List<PaymentPosting> Payments { get; set; } = new();
    public Guid? CostCenterId { get; set; }
}
