namespace ERP.Core.Models.Shared;

/// <summary>نوع الفاتورة الضريبي (ZatcaInvoiceType في الواجهة: tax_invoice|simplified|credit_note|debit_note).</summary>
public enum InvoiceType
{
    TaxInvoice = 1,              // فاتورة ضريبية (B2B)
    Simplified = 2,              // فاتورة ضريبية مبسطة (B2C)
    DebitNote = 3,               // إشعار مدين
    CreditNote = 4               // إشعار دائن
}
