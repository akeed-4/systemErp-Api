using ERP.Domain.Common;
using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

/// <summary>
/// وحدة مخزون على مستوى الشاسيه الواحد (VIN) - سيارة فعلية داخل المعرض.
/// </summary>
public class Vehicle : BaseEntity
{
    public string ChassisNumber { get; set; } = string.Empty; // VIN
    public string? EngineNumber { get; set; }
    public string? CustomsCardNumber { get; set; }

    public Guid? BrandId { get; set; }
    public Guid? AgentId { get; set; }
    public Guid? ModelId { get; set; }
    public Guid? TrimId { get; set; }

    public string BrandNameAr { get; set; } = string.Empty;
    public string ModelNameAr { get; set; } = string.Empty;
    public string? TrimNameAr { get; set; }
    public string? AgentNameAr { get; set; }

    public int Year { get; set; }
    public string ColorExterior { get; set; } = string.Empty;
    public string ColorInterior { get; set; } = string.Empty;
    public VehicleCondition Condition { get; set; }
    public decimal MileageKm { get; set; }
    public string? PlateNumber { get; set; }

    public FuelType FuelType { get; set; }
    public TransmissionType Transmission { get; set; }
    public int? Cylinders { get; set; }
    public int? EngineCapacityCc { get; set; }

    public decimal PurchasePrice { get; set; }
    public decimal AdditionalCosts { get; set; }
    public decimal PreparationCost { get; set; }
    public decimal TotalCost { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal VatAmount { get; set; }
    public decimal PriceWithVat { get; set; }
    public VatMode VatMode { get; set; }
    public decimal? MinSellingPrice { get; set; }

    public VehicleStatus Status { get; set; } = VehicleStatus.Available;
    public string Location { get; set; } = string.Empty;

    public void ChangeStatus(VehicleStatus status)
    {
        Status = status;
    }
}
