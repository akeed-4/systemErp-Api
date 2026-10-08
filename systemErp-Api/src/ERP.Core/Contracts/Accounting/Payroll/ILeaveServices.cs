using DevExtreme.AspNet.Data.ResponseModel;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Accounting;

/// <summary>أنواع الإجازات؛ أول قراءة لمنشأة بلا أنواع تنشئ الأنواع المعتادة.</summary>
public interface ILeaveTypeService : ICrudService<LeaveTypeDto, CreateLeaveTypeDto, UpdateLeaveTypeDto> { }

/// <summary>طلبات الإجازات واعتمادها وأرصدة الإجازة السنوية.</summary>
public interface ILeaveService
{
    Task<LoadResult> LoadAsync(DataSourceLoadOptions options, CancellationToken ct = default);
    Task<LeaveRequestDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<LeaveRequestDto> CreateAsync(CreateLeaveRequestDto request, CancellationToken ct = default);
    /// <summary>تعديل طلب لم يُبتّ فيه بعد.</summary>
    Task<LeaveRequestDto> UpdateAsync(Guid id, UpdateLeaveRequestDto request, CancellationToken ct = default);
    /// <summary>حذف طلب غير معتمد.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<LeaveRequestDto> ApproveAsync(Guid id, string? note, CancellationToken ct = default);
    Task<LeaveRequestDto> RejectAsync(Guid id, string? note, CancellationToken ct = default);
    /// <summary>إلغاء طلب معلّق أو معتمد؛ إجازة منقوصة الأجر دخلت مسيراً مرحّلاً لا تُلغى قبل عكسه.</summary>
    Task<LeaveRequestDto> CancelAsync(Guid id, string? note, CancellationToken ct = default);
    Task<LeaveBalanceDto> GetBalanceAsync(Guid employeeId, DateTime? asOf, CancellationToken ct = default);
    /// <summary>أرصدة الموظفين على رأس العمل حتى اليوم.</summary>
    Task<List<LeaveBalanceDto>> ListBalancesAsync(CancellationToken ct = default);
}
