using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.POS;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.POS;

public interface IPosShiftService
{
    /// <summary>وردية الكاشير الحالي المفتوحة (أو null).</summary>
    Task<PosShiftDto?> GetActiveAsync(CancellationToken ct = default);
    Task<PosShiftDto> OpenAsync(OpenShiftRequestDto request, CancellationToken ct = default);
    /// <summary>يُغلق الوردية ويحسب فرق الصندوق = الفعلي - (الافتتاحي + المبيعات النقدية - المرتجعات النقدية).</summary>
    Task<PosShiftDto> CloseAsync(CloseShiftRequestDto request, CancellationToken ct = default);
    /// <summary>تصحيح بيانات وردية (الجهاز، الرصيد الافتتاحي، جرد الإغلاق) مع إعادة حساب الفرق.</summary>
    Task<PosShiftDto> UpdateAsync(Guid id, UpdatePosShiftRequestDto request, CancellationToken ct = default);
    /// <summary>حذف وردية. إن كان لها معاملات/مرتجعات يلزم cascade=true (للأدوار الإدارية) فتُلغى كلها بعكس أثرها.</summary>
    Task DeleteAsync(Guid id, bool cascade, CancellationToken ct = default);
    Task<PagedResult<PosShiftDto>> ListAsync(PaginationParams query, CancellationToken ct = default);
    Task<PosShiftDto> GetAsync(Guid id, CancellationToken ct = default);
}
