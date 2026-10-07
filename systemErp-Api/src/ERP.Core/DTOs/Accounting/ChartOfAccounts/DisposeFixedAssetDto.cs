namespace ERP.Core.DTOs.Accounting;

/// <summary>استبعاد أصل ثابت: بيع (بمتحصَّل يدخل حساب خزينة/بنك) أو شطب (متحصَّل صفر).</summary>
public class DisposeFixedAssetDto
{
    /// <summary>تاريخ الاستبعاد (فارغ = اليوم).</summary>
    public DateTime Date { get; set; }
    public decimal Proceeds { get; set; }
    /// <summary>حساب الخزينة/البنك المستلم للمتحصَّل؛ إلزامي إن كان المتحصَّل أكبر من صفر.</summary>
    public string? TreasuryAccountCode { get; set; }
    public string? Notes { get; set; }
}
