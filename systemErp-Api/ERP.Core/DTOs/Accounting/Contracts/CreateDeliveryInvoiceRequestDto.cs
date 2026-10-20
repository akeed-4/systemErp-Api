using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

public class CreateDeliveryInvoiceRequestDto
{
    public Guid MilestoneId { get; set; }
    /// <summary>تاريخ الفاتورة (فارغ = اليوم).</summary>
    public DateTime? Date { get; set; }
}
