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

    /// <summary>صافي المستند قبل الضريبة (مخزون + خدمات)؛ عليه تُحسب ذمة المورد.</summary>
    public decimal NetAmount { get; set; }
    /// <summary>الجزء غير المخزني (خدمات ومصروفات): يُحمَّل على حساب مصروف لا على المخزون.</summary>
    public decimal ExpenseAmount { get; set; }
    public string? ExpenseAccountCode { get; set; }
    /// <summary>
    /// قيمة المخزون الفعلية إن اختلفت عن صافي الأصناف (مرتجع مشتريات يخرج بالتكلفة الحالية لا بسعر الشراء)؛
    /// الفرق يُرحَّل إلى حساب فروق أسعار المشتريات. فارغ = صافي المستند ناقص الخدمات.
    /// </summary>
    public decimal? InventoryAmount { get; set; }
    public decimal VatAmount { get; set; }
    public List<PaymentPosting> Payments { get; set; } = new();
    public Guid? CostCenterId { get; set; }
}
