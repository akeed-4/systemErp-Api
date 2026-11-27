namespace ERP.Core.DTOs.Accounting;

/// <summary>عمود تقرير: نوعه للعرض (text, number, money, percent, date, datetime, code)، والرمز (code) يُترجم ببادئته.</summary>
public class TailoringReportColumnDto
{
    public string Key { get; set; } = string.Empty;
    public string Type { get; set; } = "text";
    public string? Prefix { get; set; }
    /// <summary>يُجمع في سطر الإجمالي.</summary>
    public bool Total { get; set; }
}

/// <summary>تقرير خياطة جدولي: الأعمدة والصفوف (مفاتيحها مفاتيح الأعمدة) والإجماليات كما يحسبها الخادم.</summary>
public class TailoringReportDto
{
    public string Key { get; set; } = string.Empty;
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public List<TailoringReportColumnDto> Columns { get; set; } = new();
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
    public Dictionary<string, decimal> Totals { get; set; } = new();
}
