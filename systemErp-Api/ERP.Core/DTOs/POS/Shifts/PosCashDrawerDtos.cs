namespace ERP.Core.DTOs.POS;

/// <summary>إيداع/صرف نقدي على درج الوردية المفتوحة للكاشير الحالي.</summary>
public class CashMovementRequestDto
{
    public PosCashMovementType Type { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    /// <summary>فارغ = الافتراضي (صرف: 521 مصروفات نثرية، إيداع: حساب البنك).</summary>
    public string? CounterAccountCode { get; set; }
    /// <summary>حساب النقدية؛ فارغ = حساب الصندوق المرتبط بطريقة الدفع النقدي.</summary>
    public string? CashAccountCode { get; set; }
}

/// <summary>حسابات الحركة النقدية في نقاط البيع: الحسابات القابلة للترحيل وحساب الصندوق الافتراضي.</summary>
public class PosCashAccountsDto
{
    public string DefaultCashAccountCode { get; set; } = string.Empty;
    public List<PosAccountOptionDto> Accounts { get; set; } = new();
}

public class PosAccountOptionDto
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
}

public class PosCashMovementDto
{
    public Guid Id { get; set; }
    public Guid ShiftId { get; set; }
    public DateTime CreatedAt { get; set; }
    public PosCashMovementType Type { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string CounterAccountCode { get; set; } = string.Empty;
    public string CashAccountCode { get; set; } = string.Empty;
    public Guid? JournalEntryId { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
}

/// <summary>فئة نقدية في جرد الإغلاق (مثلاً 100 ريال × 7).</summary>
public class CashDenominationDto
{
    public decimal Value { get; set; }
    public int Count { get; set; }
}

/// <summary>تقرير الوردية: X أثناء فتحها، Z بعد إغلاقها.</summary>
public class PosShiftReportDto
{
    public string ReportType { get; set; } = "X";
    public PosShiftDto Shift { get; set; } = new();
    public DateTime GeneratedAt { get; set; }

    public int SalesCount { get; set; }
    public int VoidedCount { get; set; }
    public int ReturnsCount { get; set; }
    public decimal AverageTicket { get; set; }
    /// <summary>المبيعات قبل الضريبة بعد الخصومات.</summary>
    public decimal NetSales { get; set; }

    public decimal CashIn { get; set; }
    public decimal CashOut { get; set; }
    /// <summary>النقد المتوقع في الدرج = افتتاحي + مبيعات نقدية − مرتجعات نقدية + إيداعات − صرفيات.</summary>
    public decimal ExpectedCash { get; set; }

    public List<PosShiftReportItemDto> TopItems { get; set; } = new();
    public List<PosCashMovementDto> CashMovements { get; set; } = new();
}

public class PosShiftReportItemDto
{
    public Guid ItemId { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal Total { get; set; }
}
