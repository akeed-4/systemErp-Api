using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.CarShowroom;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.CarShowroom;

public interface IPrepaidExpenseService
{
    Task<PagedResult<PrepaidExpenseDto>> ListAsync(PaginationParams query, CancellationToken ct = default);
    /// <summary>يسجّل المصروف المدفوع مقدماً ويرحّل قيد دفعه (من حساب المصروفات المدفوعة مقدماً).</summary>
    Task<PrepaidExpenseDto> CreateAsync(CreatePrepaidExpenseDto dto, CancellationToken ct = default);
    /// <summary>يلغي مصروفاً لم يُطفأ منه شيء (يعكس قيد دفعه).</summary>
    Task<PrepaidExpenseDto> CancelAsync(Guid id, CancellationToken ct = default);
    /// <summary>ترحيل إطفاء الشهر لكل المصروفات الفعّالة. آمن للتكرار: الشهر لا يُطفأ مرتين.</summary>
    Task<AmortizePrepaidExpensesResultDto> AmortizeAsync(AmortizePrepaidExpensesDto dto, CancellationToken ct = default);
}
