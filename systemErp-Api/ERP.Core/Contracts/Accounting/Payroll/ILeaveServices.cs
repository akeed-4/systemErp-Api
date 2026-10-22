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
    /// <summary>طلب استكمال: يعيد الطلب المعلّق لصاحبه بسبب مكتوب ليعدّله ويعيد إرساله.</summary>
    Task<LeaveRequestDto> ReturnForInfoAsync(Guid id, string? note, CancellationToken ct = default);
    /// <summary>سجل حالات الطلب وتواريخها (من سجل التدقيق)؛ ownerEmployeeId يقيّده بطلبات ذلك الموظف.</summary>
    Task<List<LeaveHistoryEntryDto>> GetHistoryAsync(Guid id, Guid? ownerEmployeeId = null, CancellationToken ct = default);
    /// <summary>إلغاء طلب معلّق أو معتمد؛ إجازة منقوصة الأجر دخلت مسيراً مرحّلاً لا تُلغى قبل عكسه.</summary>
    Task<LeaveRequestDto> CancelAsync(Guid id, string? note, CancellationToken ct = default);
    Task<LeaveBalanceDto> GetBalanceAsync(Guid employeeId, DateTime? asOf, CancellationToken ct = default);
    /// <summary>أرصدة الموظفين على رأس العمل حتى اليوم.</summary>
    Task<List<LeaveBalanceDto>> ListBalancesAsync(CancellationToken ct = default);
}

/// <summary>
/// مرفقات طلب الإجازة. <c>ownerEmployeeId</c> يقيّد العملية بطلبات ذلك الموظف (بوابة الموظف): طلب غيره يُعامل
/// كغير موجود، ولا يضيف أو يحذف إلا وطلبه معلّق. بدونه العملية لشؤون الموظفين.
/// </summary>
public interface ILeaveAttachmentService
{
    Task<List<LeaveAttachmentDto>> ListAsync(Guid leaveRequestId, Guid? ownerEmployeeId = null, CancellationToken ct = default);
    Task<LeaveAttachmentDto> AddAsync(Guid leaveRequestId, string fileName, byte[] content, Guid? ownerEmployeeId = null, CancellationToken ct = default);
    Task<LeaveAttachmentFileDto> GetFileAsync(Guid leaveRequestId, Guid attachmentId, Guid? ownerEmployeeId = null, CancellationToken ct = default);
    Task DeleteAsync(Guid leaveRequestId, Guid attachmentId, Guid? ownerEmployeeId = null, CancellationToken ct = default);
}

/// <summary>
/// بوابة الموظف: دعوة الموظف لحساب دخول (من شؤون الموظفين)، وطلبات الموظف لنفسه. في عمليات الموظف يُحدَّد
/// الموظف من جلسة الدخول دائماً، فلا يقدّم أحد طلباً باسم غيره ولا يرى طلبات غيره.
/// </summary>
public interface IEmployeePortalService
{
    Task<PortalAccessDto> GetAccessAsync(Guid employeeId, CancellationToken ct = default);
    /// <summary>ينشئ حساب الموظف (بدور موظف) إن لم يوجد ويصدر رابط دعوة جديداً، ويرسله لبريده إن ضُبط البريد.</summary>
    Task<PortalAccessDto> InviteAsync(Guid employeeId, CancellationToken ct = default);
    /// <summary>يوقف دخول الموظف للبوابة ويبطل دعواته.</summary>
    Task<PortalAccessDto> RevokeAsync(Guid employeeId, CancellationToken ct = default);

    Task<MyLeaveOverviewDto> GetOverviewAsync(CancellationToken ct = default);
    Task<LeaveRequestDto> CreateLeaveAsync(CreateMyLeaveRequestDto request, CancellationToken ct = default);
    /// <summary>يلغي الموظف طلبه ما دام معلّقاً.</summary>
    Task<LeaveRequestDto> CancelLeaveAsync(Guid id, CancellationToken ct = default);
    /// <summary>يعدّل الموظف طلبه القائم، أو يعيد إرسال طلب أُعيد له للاستكمال.</summary>
    Task<LeaveRequestDto> UpdateLeaveAsync(Guid id, UpdateMyLeaveRequestDto request, CancellationToken ct = default);
    /// <summary>راتب الموظف الحالي وكشوف رواتبه في المسيرات المعتمدة.</summary>
    Task<MyPayrollDto> GetPayrollAsync(CancellationToken ct = default);
    Task<LeaveAttachmentDto> AddAttachmentAsync(Guid leaveRequestId, string fileName, byte[] content, CancellationToken ct = default);
    Task<LeaveAttachmentFileDto> GetAttachmentAsync(Guid leaveRequestId, Guid attachmentId, CancellationToken ct = default);
    Task DeleteAttachmentAsync(Guid leaveRequestId, Guid attachmentId, CancellationToken ct = default);
}
