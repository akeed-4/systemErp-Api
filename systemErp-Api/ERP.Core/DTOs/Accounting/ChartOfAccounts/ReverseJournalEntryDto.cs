namespace ERP.Core.DTOs.Accounting;

/// <summary>طلب عكس قيد: السبب إلزامي ويُحفظ في بيان القيد العكسي وسجل التدقيق.</summary>
public class ReverseJournalEntryDto
{
    public string? Reason { get; set; }
}
