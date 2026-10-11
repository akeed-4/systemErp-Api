namespace ERP.Core.DTOs.CarShowroom;

/// <summary>نقل أمر الشراء إلى المرحلة التالية مع بيانات المرحلة (إن وُجدت).</summary>
public class AdvanceProcurementRequestDto
{
    public ProcurementStage TargetStage { get; set; }
    public string? Notes { get; set; }

    // المرحلة 6: استلام الشواسيه
    public List<ReceiveVinDto> Vins { get; set; } = new();
    public bool? PdiInspectionPassed { get; set; }
    public string? WarehouseLocation { get; set; }
    public string? RejectionReason { get; set; }

    // المرحلة 7: الفوترة
    public string? SupplierInvoiceNumber { get; set; }
    public DateTime? SupplierInvoiceDate { get; set; }
}
