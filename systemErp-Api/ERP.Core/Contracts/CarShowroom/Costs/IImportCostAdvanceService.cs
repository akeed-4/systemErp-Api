using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.CarShowroom;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.CarShowroom;

/// <summary>تكاليف الاستيراد المقدمة: مسودة ← اعتماد (قيد الدفع) ← تسوية بسحبها في سندات التكاليف الإضافية ← مسوّاة. الإلغاء قبل أي سحب فقط.</summary>
public interface IImportCostAdvanceService
{
    /// <summary>Status = حالة المستند، SearchTerm = رقم المستند/الأمر/المورد. <paramref name="procurementOrderId"/> لأمر محدد.</summary>
    Task<PagedResult<ImportCostAdvanceDto>> ListAsync(PaginationParams query, Guid? procurementOrderId, CancellationToken ct = default);
    Task<ImportCostAdvanceDto> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>المفتوحة (لها رصيد) لأمر شراء: ما يسحبه سند التكاليف الإضافية.</summary>
    Task<List<ImportCostAdvanceDto>> OpenForOrderAsync(Guid procurementOrderId, CancellationToken ct = default);
    Task<ImportCostAdvanceDto> CreateAsync(CreateImportCostAdvanceDto dto, CancellationToken ct = default);
    Task<ImportCostAdvanceDto> UpdateAsync(Guid id, UpdateImportCostAdvanceDto dto, CancellationToken ct = default);
    Task<ImportCostAdvanceDto> ApproveAsync(Guid id, CancellationToken ct = default);
    Task<ImportCostAdvanceDto> CancelAsync(Guid id, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>رصيد تكاليف الاستيراد تحت التسوية حسب أمر الشراء.</summary>
    Task<List<ImportCostAdvanceBalanceDto>> BalancesAsync(CancellationToken ct = default);
}
