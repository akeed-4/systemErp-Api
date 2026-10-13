namespace ERP.Core.DTOs.CarShowroom;

public partial class AdditionalChargeAllocationDto
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    /// <summary>حصة السيارة بالعملة الأساسية. تُرسل في التوزيع اليدوي فقط، وما عداه يُحسب في السرفر.</summary>
    public decimal Amount { get; set; }
    public string? ChassisNumber { get; set; }
}
