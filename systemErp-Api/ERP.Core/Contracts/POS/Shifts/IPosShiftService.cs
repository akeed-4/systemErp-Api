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
    /// <summary>يُغلق الوردية (جرد بالفئات اختياري) ويحسب فرق الصندوق = الفعلي − النقد المتوقع، ويرحّل قيد العجز/الزيادة.</summary>
    Task<PosShiftDto> CloseAsync(CloseShiftRequestDto request, CancellationToken ct = default);
    /// <summary>إيداع/صرف نقدي على درج الوردية المفتوحة للكاشير الحالي، مع قيده.</summary>
    Task<PosCashMovementDto> AddCashMovementAsync(CashMovementRequestDto request, CancellationToken ct = default);
    Task<List<PosCashMovementDto>> ListCashMovementsAsync(Guid shiftId, CancellationToken ct = default);
    Task DeleteCashMovementAsync(Guid id, CancellationToken ct = default);
    /// <summary>تقرير X للوردية المفتوحة (shiftId = null) أو Z لوردية مغلقة.</summary>
    Task<PosShiftReportDto> GetReportAsync(Guid? shiftId, CancellationToken ct = default);
    /// <summary>تصحيح بيانات وردية (الجهاز، الرصيد الافتتاحي، جرد الإغلاق) مع إعادة حساب الفرق.</summary>
    Task<PosShiftDto> UpdateAsync(Guid id, UpdatePosShiftRequestDto request, CancellationToken ct = default);
    /// <summary>حذف وردية. إن كان لها معاملات/مرتجعات يلزم cascade=true (للأدوار الإدارية) فتُلغى كلها بعكس أثرها.</summary>
    Task DeleteAsync(Guid id, bool cascade, CancellationToken ct = default);
    Task<PagedResult<PosShiftDto>> ListAsync(PaginationParams query, CancellationToken ct = default);
    Task<PosShiftDto> GetAsync(Guid id, CancellationToken ct = default);
}
