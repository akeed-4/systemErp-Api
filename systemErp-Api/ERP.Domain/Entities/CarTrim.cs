using ERP.Domain.Common;
using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public class CarTrim : BaseEntity
{
    public Guid ModelId { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public TransmissionType? Transmission { get; set; }
    public FuelType? FuelType { get; set; }
    public int? EngineSize { get; set; }
}
