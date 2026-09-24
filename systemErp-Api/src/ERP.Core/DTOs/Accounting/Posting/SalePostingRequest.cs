using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

/// <summary>أثر بيع (فاتورة مبيعات، بيع POS، بيع سيارة). المبالغ موجبة، و IsReturn يعكس القيد.</summary>
public class SalePostingRequest
{
    public DateTime Date { get; set; } = DateTime.UtcNow;
    public string Description { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceId { get; set; }
    public string SourceNumber { get; set; } = string.Empty;
    public bool IsReturn { get; set; }

    /// <summary>حساب العميل (ذمم مدينة). فارغ = الحساب الافتراضي 112.</summary>
    public string? PartyAccountCode { get; set; }
    public string? RevenueAccountCode { get; set; }
    public string? InventoryAccountCode { get; set; }
    public string? CogsAccountCode { get; set; }

    /// <summary>الإيراد قبل الضريبة (بعد الخصم).</summary>
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    /// <summary>تكلفة البضاعة المباعة (0 = بدون قيد تكلفة).</summary>
    public decimal CostAmount { get; set; }

    public List<PaymentPosting> Payments { get; set; } = new();
    public Guid? CostCenterId { get; set; }
}
