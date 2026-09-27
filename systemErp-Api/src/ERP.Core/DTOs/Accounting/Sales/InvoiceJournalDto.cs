namespace ERP.Core.DTOs.Accounting;

/// <summary>القيد المحاسبي لفاتورة: فعلي (فاتورة مرحّلة) أو معاينة لما سيُنشأ عند الترحيل.</summary>
public class InvoiceJournalDto
{
    public bool IsPreview { get; set; }
    public string? EntryNumber { get; set; }
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<InvoiceJournalLineDto> Lines { get; set; } = new();
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }

    public decimal Subtotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    /// <summary>تكلفة البضاعة/السيارات المباعة (بيع) — مصدر قيد تكلفة الإيرادات والمخزون.</summary>
    public decimal TotalCost { get; set; }
    public decimal GrossProfit { get; set; }
}

public class InvoiceJournalLineDto
{
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? Notes { get; set; }
}
