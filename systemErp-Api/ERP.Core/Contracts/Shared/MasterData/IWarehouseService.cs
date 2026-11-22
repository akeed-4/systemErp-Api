using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface IWarehouseService : ICrudService<WarehouseDto, CreateWarehouseDto, UpdateWarehouseDto>
{
    /// <summary>ينشئ حسابات المواقع الناقصة (المخزون، تكلفة الإيراد، المبيعات، الصندوق) للمواقع القائمة غير الرئيسية. يُرجع عدد المواقع المعدَّلة.</summary>
    Task<int> EnsureSiteAccountsAsync(CancellationToken ct = default);
}
