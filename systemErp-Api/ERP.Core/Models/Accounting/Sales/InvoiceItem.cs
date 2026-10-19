namespace ERP.Core.Models.Accounting;

/// <summary>بند فاتورة معارض السيارات (جدول InvoiceItem كما هو).</summary>
public class InvoiceItem : InvoiceItemBase
{
    public virtual Invoice Invoice { get; set; } = null!;
}
