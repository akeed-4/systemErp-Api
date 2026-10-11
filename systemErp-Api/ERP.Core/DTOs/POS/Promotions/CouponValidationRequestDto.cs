namespace ERP.Core.DTOs.POS;

public class CouponValidationRequestDto
{
    public string Code { get; set; } = string.Empty;
    public decimal CartAmount { get; set; }
}
