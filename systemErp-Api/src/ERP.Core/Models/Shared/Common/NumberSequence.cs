namespace ERP.Core.Models.Shared;

/// <summary>عدّاد ترقيم المستندات لكل منشأة (فاتورة، قيد، سند، ...). يُزاد داخل معاملة قاعدة البيانات.</summary>
public class NumberSequence : BaseEntity
{
    /// <summary>مثال: sales_invoice, journal_entry, receipt_voucher</summary>
    public string Key { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
    public long LastValue { get; set; }
    public int Padding { get; set; } = 6;
    /// <summary>رمز تزامن متفائل: يتغيّر مع كل تعديل فيمنع إصدار رقمين متطابقين عند الطلبات المتزامنة.</summary>
    public Guid Version { get; set; } = Guid.NewGuid();
}
