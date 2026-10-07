using ERP.Core.DTOs.Accounting;

namespace ERP.Core.Contracts.Accounting;

/// <summary>إدارة الفترات المالية: تاريخ إقفال الدفاتر وإقفال السنة المالية بقيد يرحّل نتيجتها إلى الأرباح المبقاة.</summary>
public interface IFiscalPeriodService
{
    Task<FiscalPeriodStatusDto> GetAsync(CancellationToken ct = default);
    /// <summary>يضبط تاريخ إقفال الدفاتر (null = فتح كل ما بعد آخر سنة مقفلة). لا يُرجَع قبل نهاية سنة مقفلة.</summary>
    Task<FiscalPeriodStatusDto> SetLockAsync(SetPeriodLockDto request, CancellationToken ct = default);
    /// <summary>يقفل السنة المالية المنتهية: قيد إقفال للإيرادات والمصروفات إلى الأرباح المبقاة ثم إقفال الدفاتر حتى نهايتها.</summary>
    Task<YearClosingResultDto> CloseYearAsync(CloseYearRequestDto request, CancellationToken ct = default);
}
