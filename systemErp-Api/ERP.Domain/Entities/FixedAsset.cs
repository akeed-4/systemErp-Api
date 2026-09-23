using ERP.Domain.Common;

namespace ERP.Domain.Entities;

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
}
