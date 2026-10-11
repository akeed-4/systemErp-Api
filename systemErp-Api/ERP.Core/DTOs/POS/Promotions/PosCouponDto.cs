namespace ERP.Core.DTOs.POS;

public partial class PosCouponDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string Code { get; set; } = string.Empty;
    public string TitleAr { get; set; } = string.Empty;
    public PosDiscountType DiscountType { get; set; }
    public decimal DiscountValue { get; set; }
    public decimal MinCartAmount { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public int UsageCount { get; set; }
    public int? UsageLimit { get; set; }
    public bool IsActive { get; set; } = true;
}
