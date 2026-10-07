
namespace ERP.Core.Models.Accounting;

public class FixedAsset : BaseEntity
{
    public string AssetCode { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public DateTime PurchaseDate { get; set; }
    public decimal PurchaseCost { get; set; }
    public decimal CurrentBookValue { get; set; }
    public decimal? AccumulatedDepreciation { get; set; }
    public decimal DepreciationRate { get; set; }
    /// <summary>القيمة التخريدية: لا يُهلك الأصل دونها، وقسط الإهلاك يُحسب على التكلفة ناقصها.</summary>
    public decimal SalvageValue { get; set; }
    /// <summary>تاريخ استبعاد الأصل (بيع/شطب)؛ بعده لا يُهلك ولا يُعدَّل.</summary>
    public DateTime? DisposedAt { get; set; }
    public decimal? DisposalProceeds { get; set; }
    public Guid? DisposalJournalEntryId { get; set; }
    /// <summary>قيد اقتناء الأصل (مدين حساب الأصل)؛ فارغ إن قُيِّد الاقتناء خارج شاشة الأصول (فاتورة شراء أو قيد يدوي).</summary>
    public Guid? AcquisitionJournalEntryId { get; set; }
    public Guid AssetAccountId { get; set; }
    public Guid AccumulatedDepreciationAccountId { get; set; }
    /// <summary>حساب مصروف الإهلاك (مدين قيد الإهلاك). فارغ للأصول المسجَّلة قبل إضافة الإهلاك حتى تُعدَّل.</summary>
    public Guid? DepreciationExpenseAccountId { get; set; }
    /// <summary>مركز التكلفة (النشاط) المسؤول عن الأصل ويتحمّل مصروف إهلاكه. إلزامي للأصول الجديدة، وفارغ للقديمة حتى يُحدَّد يدوياً.</summary>
    public Guid? CostCenterId { get; set; }
    /// <summary>المستودع/المعرض الذي يوجد فيه الأصل (اختياري). يُحفظ مع كل إهلاك مرحَّل ويُرشَّح به ترحيل الإهلاك.</summary>
    public Guid? WarehouseId { get; set; }
}
