using ERP.Core.Models.Shared;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

/// <summary>أثر بيع (فاتورة مبيعات، بيع POS، بيع سيارة). المبالغ موجبة، و IsReturn يعكس القيد.</summary>
public class SalePostingRequest
{
    public string? Module { get; set; }
    /// <summary>تعديل مستند مرحّل: يُستبدل هذا القيد في مكانه (نفس رقمه) بدل إنشاء قيد جديد.</summary>
    public Guid? ReplaceEntryId { get; set; }
    public DateTime Date { get; set; } = SaudiTime.Today;
    public string Description { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceId { get; set; }
    public string SourceNumber { get; set; } = string.Empty;
    public bool IsReturn { get; set; }

    /// <summary>حساب العميل (ذمم مدينة). فارغ = الحساب الافتراضي 112.</summary>
    public string? PartyAccountCode { get; set; }
    public string? RevenueAccountCode { get; set; }
    public string? InventoryAccountCode { get; set; }
    /// <summary>مستودع المستند: حساب مخزونه (إن حُدِّد) بعد الحساب اليدوي أعلاه وقبل حساب الإعدادات. فارغ = الافتراضي.</summary>
    public Guid? WarehouseId { get; set; }
    public string? CogsAccountCode { get; set; }

    /// <summary>الإيراد قبل الضريبة (بعد الخصم).</summary>
    public decimal NetAmount { get; set; }
    /// <summary>توزيع الإيراد على حسابات ومراكز تكلفة (مجموعه = NetAmount). فارغ = سطر واحد بحساب الإيراد ومركز التكلفة أدناه.</summary>
    public List<RevenuePosting> RevenueLines { get; set; } = new();
    public decimal ExciseTaxAmount { get; set; }
    public decimal VatAmount { get; set; }
    /// <summary>تكلفة البضاعة المباعة (0 = بدون قيد تكلفة؛ السالب يعكس الاتجاه: مرتجع أعاد تسعير الرصيد بأقل من قيمته).</summary>
    public decimal CostAmount { get; set; }
    /// <summary>توزيع التكلفة على مراكز تكلفة الأسطر (مجموعه = CostAmount). فارغ = سطر تكلفة واحد بلا مركز.</summary>
    public List<CostCenterAmount> CostLines { get; set; } = new();

    public List<PaymentPosting> Payments { get; set; } = new();
    public Guid? CostCenterId { get; set; }
}
