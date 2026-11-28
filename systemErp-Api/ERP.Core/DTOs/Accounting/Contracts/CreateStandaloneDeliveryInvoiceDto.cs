namespace ERP.Core.DTOs.Accounting;

/// <summary>فاتورة مرحلية (بيع أو شراء) لبيانات تسليم/استلام مستقلة غير مفوترة لطرف واحد.</summary>
public class CreateStandaloneDeliveryInvoiceDto
{
    /// <summary>العميل (بيانات التسليم) أو المورد (بيانات الاستلام).</summary>
    public Guid PartyId { get; set; }
    public List<Guid> DeliveryNoteIds { get; set; } = new();
    /// <summary>تاريخ الفاتورة (فارغ = اليوم).</summary>
    public DateTime? Date { get; set; }
}
