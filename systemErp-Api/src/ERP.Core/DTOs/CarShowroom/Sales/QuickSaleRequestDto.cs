namespace ERP.Core.DTOs.CarShowroom;

/// <summary>بيانات التسليم الاختيارية عند إكمال العقد حتى الفوترة. الفارغ يُملأ افتراضيًا من بيانات المشتري.</summary>
public class CompleteSalesContractRequestDto
{
    public string? HandoverProtocolNumber { get; set; }
    public string? HandoverSignee { get; set; }
    public string? HandoverSigneeNationalId { get; set; }
    public DateTime? DeliveryDate { get; set; }
    public string? DeliveryLocation { get; set; }
    public string? PdiInspectionNotes { get; set; }
    public string? Notes { get; set; }
}

/// <summary>بيع سريع: إنشاء العقد ثم اعتماده وتخصيصه وتسليمه وفوترته في معاملة واحدة.</summary>
public class QuickSaleRequestDto
{
    public CreateCarSalesContractDto Contract { get; set; } = new();
    public CompleteSalesContractRequestDto? Handover { get; set; }
}
