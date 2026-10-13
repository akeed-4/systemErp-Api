namespace ERP.Core.DTOs.CarShowroom;

/// <summary>نقل أمر الشراء إلى المرحلة التالية مع بيانات المرحلة (إن وُجدت).</summary>
public class AdvanceProcurementRequestDto
{
    public ProcurementStage TargetStage { get; set; }
    public string? Notes { get; set; }

    // المرحلة 6: استلام الشواسيه
    public List<ReceiveVinDto> Vins { get; set; } = new();
    /// <summary>استلام جزئي: يُقبل عدد أقل من المتبقي، ويبقى الأمر في مرحلة أمر الشراء حتى يكتمل استلام الكمية.</summary>
    public bool PartialReceipt { get; set; }
    public bool? PdiInspectionPassed { get; set; }
    public string? WarehouseLocation { get; set; }
    public string? RejectionReason { get; set; }

    // المرحلة 7: الفوترة
    public string? SupplierInvoiceNumber { get; set; }
    public DateTime? SupplierInvoiceDate { get; set; }
}
