namespace ERP.Core.DTOs.Accounting;

/// <summary>أعمار الديون بالعملة الأساسية حتى تاريخ معيّن.</summary>
public class AgingReportDto
{
    public DateTime AsOf { get; set; }
    /// <summary>receivable | payable</summary>
    public string Type { get; set; } = "receivable";
    public List<AgingRowDto> Rows { get; set; } = new();
    public AgingRowDto Totals { get; set; } = new();
}

public class AgingRowDto
{
    public Guid? PartyId { get; set; }
    public string PartyName { get; set; } = string.Empty;
    public string? AccountCode { get; set; }
    /// <summary>المتبقي على فواتير عمرها حتى 30 يوماً.</summary>
    public decimal Days0To30 { get; set; }
    public decimal Days31To60 { get; set; }
    public decimal Days61To90 { get; set; }
    public decimal Over90 { get; set; }
    /// <summary>إجمالي المتبقي على الفواتير.</summary>
    public decimal TotalDue { get; set; }
    /// <summary>سندات على حساب الطرف لم تُوزَّع على فواتير (دفعات مقدمة).</summary>
    public decimal Unallocated { get; set; }
    /// <summary>الصافي = المتبقي − غير الموزّع.</summary>
    public decimal Net { get; set; }
    public int OpenInvoices { get; set; }
}
