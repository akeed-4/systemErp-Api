namespace ERP.Core.DTOs.Accounting;

public partial class CreateFixedAssetDto
{
    public string AssetCode { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public DateTime PurchaseDate { get; set; }
    public decimal PurchaseCost { get; set; }
    public decimal CurrentBookValue { get; set; }
    public decimal? AccumulatedDepreciation { get; set; }
    public decimal DepreciationRate { get; set; }
    public decimal SalvageValue { get; set; }
    /// <summary>
    /// عند التسجيل: الحساب الذي مُوِّل منه الاقتناء (صندوق/بنك/مورد، أو الأرصدة الافتتاحية لأصل قائم) فيُرحَّل قيد الاقتناء.
    /// فارغ = الاقتناء مقيَّد خارج شاشة الأصول ولا قيد هنا.
    /// </summary>
    public string? AcquisitionAccountCode { get; set; }
    public Guid AssetAccountId { get; set; }
    public Guid AccumulatedDepreciationAccountId { get; set; }
    public Guid? DepreciationExpenseAccountId { get; set; }
    public Guid? CostCenterId { get; set; }
    public Guid? WarehouseId { get; set; }
}
