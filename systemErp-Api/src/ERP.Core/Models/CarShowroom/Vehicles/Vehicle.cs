namespace ERP.Core.Models.CarShowroom;

/// <summary>
/// وحدة مخزون على مستوى الشاسيه (VIN) - سيارة فعلية داخل المعرض. كيان مستقل عن الأصناف (Product)
/// لأن السيارة تُتتبَّع بهويتها لا بكميتها.
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
    public Guid? YearId { get; set; }
    public string BrandNameAr { get; set; } = string.Empty;
    public string ModelNameAr { get; set; } = string.Empty;
    public string? Trim { get; set; }
    public string? TrimNameAr { get; set; }
    public string? AgentNameAr { get; set; }
    public string? ImageUrl { get; set; }
    public int Year { get; set; }

    public string ColorExterior { get; set; } = string.Empty;
    public string ColorInterior { get; set; } = string.Empty;
    /// <summary>leather | fabric | nappa | velour</summary>
    public string? InteriorMaterial { get; set; }
    public VehicleCondition Condition { get; set; } = VehicleCondition.New;
    public decimal? MileageKm { get; set; }
    public string? PlateNumber { get; set; }
    public string? PlateNumberAr { get; set; }
    public string? PlateLettersAr { get; set; }
    /// <summary>new | transferred | export | pending</summary>
    public string? PlateRegistrationStatus { get; set; }
    public FuelType FuelType { get; set; } = FuelType.Petrol;
    public TransmissionType Transmission { get; set; } = TransmissionType.Automatic;
    public int? Cylinders { get; set; }
    public int? EngineCapacityCc { get; set; }

    public decimal? PurchasePrice { get; set; }
    public decimal? AdditionalCosts { get; set; }
    public decimal? PreparationCost { get; set; }
    public decimal TotalCost { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal? VatAmount { get; set; }
    public decimal? PriceWithVat { get; set; }
    public VatMode VatMode { get; set; } = VatMode.Standard_15;
    public decimal? MinSellingPrice { get; set; }

    public VehicleStatus Status { get; set; } = VehicleStatus.Available;
    public string Location { get; set; } = string.Empty;
    public string? QrCodeUrl { get; set; }
    public string? Notes { get; set; }
    public int? WarrantyYears { get; set; }
    public int? WarrantyKm { get; set; }
    public VehiclePdiChecklist? PdiChecklist { get; set; }

    /// <summary>أمر الشراء الذي وردت منه المركبة (إن وُجد).</summary>
    public Guid? ProcurementOrderId { get; set; }
}
