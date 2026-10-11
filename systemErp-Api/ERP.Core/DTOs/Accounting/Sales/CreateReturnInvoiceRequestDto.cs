using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

public class CreateReturnInvoiceRequestDto
{
    public Guid OriginalInvoiceId { get; set; }
    public string ReturnReason { get; set; } = string.Empty;
    /// <summary>طريقة رد المبلغ (فارغ = نفس طريقة دفع الفاتورة الأصلية).</summary>
    public PaymentMethod? RefundPaymentMethod { get; set; }
    /// <summary>الأصناف والكميات المرتجعة. فارغ = مرتجع كلي.</summary>
    public List<ReturnLineDto> Lines { get; set; } = new();
}
