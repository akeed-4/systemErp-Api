using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface IPlatformAccessService
{
    /// <summary>صلاحية المستخدم الحالي على لوحة المنصة: من إعداد Platform:AdminEmails أو من صلاحية ممنوحة له بالاسم.</summary>
    Task<PlatformAccessDto> GetAsync(CancellationToken ct = default);
}
