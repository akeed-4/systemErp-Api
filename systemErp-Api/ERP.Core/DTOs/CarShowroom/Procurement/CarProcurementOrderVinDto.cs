namespace ERP.Core.DTOs.CarShowroom;

public partial class CarProcurementOrderVinDto
{
    public Guid Id { get; set; }
    public Guid? ItemId { get; set; }
    public string Vin { get; set; } = string.Empty;
    public string? EngineNumber { get; set; }
    public string? CustomsCardNumber { get; set; }
    public Guid? VehicleId { get; set; }
}
