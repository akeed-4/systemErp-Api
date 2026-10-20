using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

public class CreateReturnInvoiceRequestDto
{
    public Guid OriginalInvoiceId { get; set; }
    public string ReturnReason { get; set; } = string.Empty;
    /// <summary>طريقة رد المبلغ (فارغ = نفس طريقة دفع الفاتورة الأصلية).</summary>
    public PaymentMethod? RefundPaymentMethod { get; set; }
    /// <summary>رمز طريقة الرد المؤسَّسة (يُرحَّل على حسابها) مع RefundPaymentMethod.</summary>
    public string? RefundPaymentMethodCode { get; set; }
    /// <summary>
    /// الرد بأكثر من طريقة (يُقدَّم على RefundPaymentMethod): المبالغ تُوزَّع بنسبتها على إجمالي المرتجع المحسوب في السرفر،
    /// والكسر على آخر سطر، وكل سطر يُرحَّل على حساب طريقته.
    /// </summary>
    public List<InvoicePaymentSplitDto> RefundSplits { get; set; } = new();
    /// <summary>الأصناف والكميات المرتجعة. فارغ = مرتجع كلي.</summary>
    public List<ReturnLineDto> Lines { get; set; } = new();
    /// <summary>فاتورة سيارات: المركبات المرتجعة (فارغ = كل ما لم يُرجَع منها).</summary>
    public List<Guid> VehicleIds { get; set; } = new();
    /// <summary>تاريخ المرتجع (فارغ = الآن).</summary>
    public DateTime? ReturnDate { get; set; }
    /// <summary>ملاحظات المرتجع (تُضاف إلى سبب الإرجاع في سجل المستند).</summary>
    public string? Notes { get; set; }
}
