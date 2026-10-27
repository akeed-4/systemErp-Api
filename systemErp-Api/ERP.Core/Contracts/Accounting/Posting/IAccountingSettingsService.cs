using ERP.Core.DTOs.Accounting;

namespace ERP.Core.Contracts.Accounting;

public interface IAccountingSettingsService
{
    Task<AccountingSettingsDto> GetAsync(CancellationToken ct = default);
    Task<AccountingSettingsDto> SaveAsync(SaveAccountingSettingsDto settings, CancellationToken ct = default);
}
