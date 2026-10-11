namespace ERP.Core.Models.CarShowroom;

/// <summary>شاسيه (VIN) وُرد ضمن أمر شراء - يربط بين الأمر والمركبة المُنشأة.</summary>
public class CarProcurementOrderVin : BaseEntity
{
    public Guid OrderId { get; set; }
    public Guid? ItemId { get; set; }
    public string Vin { get; set; } = string.Empty;
    public string? EngineNumber { get; set; }
    public string? CustomsCardNumber { get; set; }
    public Guid? VehicleId { get; set; }

    public virtual CarProcurementOrder Order { get; set; } = null!;
}
