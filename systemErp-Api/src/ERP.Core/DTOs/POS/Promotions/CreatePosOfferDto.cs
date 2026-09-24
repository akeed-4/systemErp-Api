namespace ERP.Core.DTOs.POS;

public partial class CreatePosOfferDto
{
    public string TitleAr { get; set; } = string.Empty;
    public PosOfferType Type { get; set; }
    public decimal? DiscountPercent { get; set; }
    public decimal? DiscountAmount { get; set; }
    public int? BuyQuantity { get; set; }
    public int? GetQuantity { get; set; }
    public Guid? TargetCategoryId { get; set; }
    public Guid? TargetItemId { get; set; }
    public bool IsActive { get; set; } = true;
    public string BadgeText { get; set; } = string.Empty;
}
