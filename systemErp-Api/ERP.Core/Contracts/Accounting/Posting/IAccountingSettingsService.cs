using ERP.Core.DTOs.Accounting;

namespace ERP.Core.Contracts.Accounting;

public interface IAccountingSettingsService
{
    Task<AccountingSettingsDto> GetAsync(CancellationToken ct = default);
    Task<AccountingSettingsDto> SaveAsync(SaveAccountingSettingsDto settings, CancellationToken ct = default);
    /// <summary>ينشئ حسابات العقود الناقصة (أو يربط القائم المناسب) ويربطها بالإعدادات غير المحددة؛ لا يغيّر اختيار المستخدم.</summary>
    Task<ContractAccountsSetupDto> InitializeContractAccountsAsync(CancellationToken ct = default);
}
