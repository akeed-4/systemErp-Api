namespace ERP.Core.DTOs.CarShowroom;

public partial class VehiclePdiChecklistDto
{
    public bool ExteriorClean { get; set; }
    public bool InteriorClean { get; set; }
    public bool FluidLevelsChecked { get; set; }
    public bool BatteryTested { get; set; }
    public bool TiresPressureChecked { get; set; }
    public bool AccessoriesInstalled { get; set; }
    public string? Notes { get; set; }
    public string? InspectedBy { get; set; }
    public DateTime? InspectedAt { get; set; }
}
