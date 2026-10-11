namespace ERP.Core.DTOs.CarShowroom;

public class AdvanceSalesContractRequestDto
{
    public SalesContractStatus TargetStatus { get; set; }

    // التخصيص (allocated)
    /// <summary>مركبة بديلة عند التخصيص (اختياري). فارغ = مركبة العقد الحالية.</summary>
    public Guid? VehicleId { get; set; }
    public string? PdiInspectionNotes { get; set; }
    public VehiclePdiChecklistDto? PdiChecklist { get; set; }

    // التسليم (delivered)
    public string? HandoverProtocolNumber { get; set; }
    public string? HandoverSignee { get; set; }
    public string? HandoverSigneeNationalId { get; set; }
    public DateTime? DeliveryDate { get; set; }
    public string? DeliveryLocation { get; set; }

    public string? Notes { get; set; }
}
