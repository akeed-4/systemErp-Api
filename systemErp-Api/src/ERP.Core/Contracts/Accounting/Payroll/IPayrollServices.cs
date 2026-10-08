using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;

namespace ERP.Core.Contracts.Accounting;

public interface IEmployeeService : ICrudService<EmployeeDto, CreateEmployeeDto, UpdateEmployeeDto>
{
    /// <summary>وثائق وعقود الموظفين على رأس العمل التي انتهت أو تنتهي خلال عدد الأيام المحدد، الأقرب أولاً.</summary>
    Task<List<EmployeeAlertDto>> GetExpiringAsync(int days, CancellationToken ct = default);
    Task<EmployeeSummaryDto> GetSummaryAsync(CancellationToken ct = default);
}

public interface IDepartmentService : ICrudService<DepartmentDto, CreateDepartmentDto, UpdateDepartmentDto> { }

/// <summary>مسير الرواتب الشهري: معاينة، ترحيل بقيد واحد، سجل، وعكس.</summary>
public interface IPayrollService
{
    Task<PayrollRunDto> PreviewAsync(PayrollRunRequestDto request, CancellationToken ct = default);
    Task<PayrollRunDto> PostAsync(PayrollRunRequestDto request, CancellationToken ct = default);
    Task<List<PayrollRunDto>> ListAsync(CancellationToken ct = default);
    Task<PayrollRunDto> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>يعكس قيد المسير ويفتح الشهر لمسير جديد.</summary>
    Task<PayrollRunDto> ReverseAsync(Guid id, CancellationToken ct = default);
}
