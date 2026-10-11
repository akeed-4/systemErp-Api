
namespace ERP.Core.Models.Accounting;

/// <summary>
/// توزيع جزء من سند قبض/صرف على فاتورة آجلة بعينها (سداد جزئي أو كلي). ما لم يُوزَّع من السند دفعة مقدمة على حساب الطرف.
/// منه يُحسب المتبقي على كل فاتورة وأعمار الديون.
/// </summary>
public class VoucherAllocation : BaseEntity
{
    public Guid VoucherId { get; set; }
    public Guid InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }

    public virtual Voucher Voucher { get; set; } = null!;
}
