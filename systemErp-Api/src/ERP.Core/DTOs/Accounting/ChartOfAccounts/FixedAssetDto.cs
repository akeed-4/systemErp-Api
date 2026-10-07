namespace ERP.Core.DTOs.Accounting;

public partial class FixedAssetDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string AssetCode { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public DateTime PurchaseDate { get; set; }
    public decimal PurchaseCost { get; set; }
    public decimal CurrentBookValue { get; set; }
    public decimal? AccumulatedDepreciation { get; set; }
    public decimal DepreciationRate { get; set; }
    public decimal SalvageValue { get; set; }
    public DateTime? DisposedAt { get; set; }
    public decimal? DisposalProceeds { get; set; }
    public Guid? DisposalJournalEntryId { get; set; }
    public Guid? AcquisitionJournalEntryId { get; set; }
    public Guid AssetAccountId { get; set; }
    public Guid AccumulatedDepreciationAccountId { get; set; }
    public Guid? DepreciationExpenseAccountId { get; set; }
    public Guid? CostCenterId { get; set; }
    public Guid? WarehouseId { get; set; }
}
