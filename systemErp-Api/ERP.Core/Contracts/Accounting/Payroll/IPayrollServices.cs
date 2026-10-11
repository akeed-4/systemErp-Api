using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Accounting;

public interface IEmployeeService : ICrudService<EmployeeDto, CreateEmployeeDto, UpdateEmployeeDto>
{
    /// <summary>وثائق وعقود الموظفين على رأس العمل التي انتهت أو تنتهي خلال عدد الأيام المحدد، الأقرب أولاً.</summary>
    Task<List<EmployeeAlertDto>> GetExpiringAsync(int days, CancellationToken ct = default);
    Task<EmployeeSummaryDto> GetSummaryAsync(CancellationToken ct = default);
}

public interface IDepartmentService : ICrudService<DepartmentDto, CreateDepartmentDto, UpdateDepartmentDto> { }

/// <summary>الحضور اليومي: كشف يوم، استيراد جماعي، وملخص شهري بأثره على المسير.</summary>
public interface IAttendanceService
{
    Task<List<AttendanceDayRowDto>> GetDayAsync(DateTime date, CancellationToken ct = default);
    Task<List<AttendanceDayRowDto>> SaveDayAsync(SaveAttendanceDayDto request, CancellationToken ct = default);
    Task<ImportResultDto> ImportAsync(List<AttendanceImportRowDto> rows, CancellationToken ct = default);
    Task<List<AttendanceSummaryRowDto>> GetSummaryAsync(string period, CancellationToken ct = default);
}

/// <summary>مسير الرواتب الشهري: معاينة، مسودة تُراجَع ثم تُعتمد وتُرحَّل بقيد واحد، سجل، عكس، وملف حماية الأجور.</summary>
public interface IPayrollService
{
    Task<PayrollRunDto> PreviewAsync(PayrollRunRequestDto request, CancellationToken ct = default);
    /// <summary>يحفظ مسير الشهر مسودة (أو يعيد حساب مسودته الموجودة) دون أثر محاسبي.</summary>
    Task<PayrollRunDto> SaveDraftAsync(PayrollRunRequestDto request, CancellationToken ct = default);
    /// <summary>يعتمد المسودة ويرحّلها. إن تغيّرت بيانات الشهر منذ حفظها يُرفض حتى تُعاد حسابها.</summary>
    Task<PayrollRunDto> ApproveAsync(Guid id, CancellationToken ct = default);
    Task DeleteDraftAsync(Guid id, CancellationToken ct = default);
    /// <summary>ترحيل مباشر بلا مسودة.</summary>
    Task<PayrollRunDto> PostAsync(PayrollRunRequestDto request, CancellationToken ct = default);
    Task<List<PayrollRunDto>> ListAsync(CancellationToken ct = default);
    Task<PayrollRunDto> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>يعكس قيد المسير ويفتح الشهر لمسير جديد.</summary>
    Task<PayrollRunDto> ReverseAsync(Guid id, CancellationToken ct = default);
    /// <summary>ملف رواتب مسير مرحّل لرفعه للبنك (حماية الأجور) بصيغة CSV عامة.</summary>
    Task<TextFileDto> GetWageFileAsync(Guid id, CancellationToken ct = default);
}
