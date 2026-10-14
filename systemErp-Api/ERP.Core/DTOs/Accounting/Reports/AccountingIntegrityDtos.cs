namespace ERP.Core.DTOs.Accounting;

/// <summary>حساب يختلف رصيده المخزَّن عن مجموع حركاته في القيود.</summary>
public class BalanceDriftRowDto
{
    public string AccountCode { get; set; } = string.Empty;
    public string AccountNameAr { get; set; } = string.Empty;
    public string AccountNameEn { get; set; } = string.Empty;
    /// <summary>الرصيد المخزَّن على الحساب (قبل الإصلاح).</summary>
    public decimal StoredBalance { get; set; }
    /// <summary>الرصيد من مجموع سطور القيود (المصدر الصحيح).</summary>
    public decimal LedgerBalance { get; set; }
    public decimal Difference { get; set; }
}

/// <summary>فحص «رصيد كل حساب = مجموع حركاته»، أو نتيجة إعادة احتسابه.</summary>
public class BalanceIntegrityDto
{
    public int AccountsChecked { get; set; }
    public int DriftedCount { get; set; }
    public decimal TotalAbsoluteDifference { get; set; }
    /// <summary>true إن أُعيد احتساب الأرصدة (الصفوف تعرض ما كان قبل الإصلاح).</summary>
    public bool Rebuilt { get; set; }
    public bool IsConsistent => DriftedCount == 0;
    public List<BalanceDriftRowDto> Rows { get; set; } = new();
}

/// <summary>مقارنة قيمة مخزون من سجلّه التفصيلي بأرصدة حساباته في الأستاذ.</summary>
public class InventoryReconciliationRowDto
{
    /// <summary>items | vehicles</summary>
    public string Key { get; set; } = string.Empty;
    public string LabelAr { get; set; } = string.Empty;
    public string LabelEn { get; set; } = string.Empty;
    /// <summary>القيمة من السجل التفصيلي: الكمية × متوسط التكلفة للأصناف، وتكلفة السيارات في المخزون.</summary>
    public decimal SubledgerValue { get; set; }
    /// <summary>مجموع أرصدة حسابات المخزون المقابلة من سطور القيود.</summary>
    public decimal LedgerValue { get; set; }
    public decimal Difference { get; set; }
    public bool IsReconciled { get; set; }
    public List<string> AccountCodes { get; set; } = new();
}

public class InventoryReconciliationDto
{
    public DateTime AsOf { get; set; }
    public bool IsReconciled { get; set; }
    public List<InventoryReconciliationRowDto> Rows { get; set; } = new();
}
