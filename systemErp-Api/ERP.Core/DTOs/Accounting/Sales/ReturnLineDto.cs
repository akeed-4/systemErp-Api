using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

public class ReturnLineDto
{
    public Guid ItemId { get; set; }
    /// <summary>معرّف السطر في الفاتورة الأصلية؛ مطلوب لتمييز سطور الصنف المتكررة أو خدمات الإيجار.</summary>
    public Guid? OriginalInvoiceItemId { get; set; }
    public decimal Quantity { get; set; }
}
