namespace ERP.Core.DTOs.POS;

public class CouponValidationResultDto
{
    public bool IsValid { get; set; }
    public string? Message { get; set; }
    public decimal DiscountAmount { get; set; }
    public PosCouponDto? Coupon { get; set; }
}
