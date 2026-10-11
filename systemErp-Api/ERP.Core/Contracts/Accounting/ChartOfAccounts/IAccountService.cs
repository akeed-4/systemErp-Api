using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Accounting;

public interface IAccountService : ICrudService<AccountDto, CreateAccountDto, UpdateAccountDto>
{
    /// <summary>شجرة الحسابات كاملة (مع الأبناء) لعرضها في الواجهة.</summary>
    Task<List<AccountDto>> GetTreeAsync(CancellationToken ct = default);
    Task<AccountDto> GetByCodeAsync(string code, CancellationToken ct = default);
    /// <summary>ينشئ حساباً فرعياً تلقائياً لعميل/مورد/بنك تحت الحساب الأب (112 / 211 / 111) ويُرجع كوده. آمن للتكرار.</summary>
    Task<string> EnsureLinkedAccountAsync(LinkedEntityType type, Guid entityId, string nameAr, string nameEn, CancellationToken ct = default);
}
