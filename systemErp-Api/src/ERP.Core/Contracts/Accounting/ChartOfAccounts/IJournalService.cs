using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Accounting;

public interface IJournalService
{
    Task<PagedResult<JournalEntryDto>> ListAsync(PaginationParams query, CancellationToken ct = default);
    Task<JournalEntryDto> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>قيد يدوي مرحَّل مباشرة. يجب أن يتوازن المدين والدائن.</summary>
    Task<JournalEntryDto> CreateManualAsync(CreateJournalEntryDto request, CancellationToken ct = default);
    /// <summary>تعديل قيد (اليدوي أو الآلي؛ الآلي يُعلَّم في وصفه). قيد العكس والمعكوس لا يُعدَّلان.</summary>
    Task<JournalEntryDto> UpdateManualAsync(Guid id, UpdateJournalEntryDto request, CancellationToken ct = default);
    Task DeleteManualAsync(Guid id, CancellationToken ct = default);
    Task<JournalEntryDto> ReverseAsync(Guid id, CancellationToken ct = default);
}
