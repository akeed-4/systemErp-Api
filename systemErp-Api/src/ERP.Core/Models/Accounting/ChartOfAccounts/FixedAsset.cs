
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
    public Guid AssetAccountId { get; set; }
    public Guid AccumulatedDepreciationAccountId { get; set; }
    /// <summary>حساب مصروف الإهلاك (مدين قيد الإهلاك). فارغ للأصول المسجَّلة قبل إضافة الإهلاك حتى تُعدَّل.</summary>
    public Guid? DepreciationExpenseAccountId { get; set; }
    /// <summary>مركز التكلفة (النشاط) المسؤول عن الأصل ويتحمّل مصروف إهلاكه. إلزامي للأصول الجديدة، وفارغ للقديمة حتى يُحدَّد يدوياً.</summary>
    public Guid? CostCenterId { get; set; }
}
