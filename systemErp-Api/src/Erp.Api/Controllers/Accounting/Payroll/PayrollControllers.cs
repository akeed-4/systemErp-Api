using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>ملفات الموظفين، مع تنبيهات انتهاء الوثائق والعقود ومؤشرات شؤون الموظفين.</summary>
[Route("api/v1/employees"), RequireScreen("hr"), RequireModule(PlatformModules.Hr)]
public class EmployeesController : CrudController<EmployeeDto, CreateEmployeeDto, UpdateEmployeeDto>
{
    private const int DefaultAlertDays = 60;
    private readonly IEmployeeService _employees;
    public EmployeesController(IEmployeeService s) : base(s) => _employees = s;

    /// <summary>هويات وجوازات وعقود انتهت أو تنتهي خلال days يوماً، وفترات تجربة تنتهي خلالها.</summary>
    [HttpGet("expiring")]
    public async Task<IActionResult> Expiring([FromQuery] int? days, CancellationToken ct)
        => Success(await _employees.GetExpiringAsync(days ?? DefaultAlertDays, ct));

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct) => Success(await _employees.GetSummaryAsync(ct));
}

[Route("api/v1/departments"), RequireScreen("hr"), RequireModule(PlatformModules.Hr)]
public class DepartmentsController : CrudController<DepartmentDto, CreateDepartmentDto, UpdateDepartmentDto>
{
    public DepartmentsController(IDepartmentService s) : base(s) { }
}

/// <summary>الحضور اليومي: كشف يوم لكل الموظفين، استيراد جماعي، وملخص شهري.</summary>
[Route("api/v1/attendance"), RequireScreen("hr"), RequireModule(PlatformModules.Hr)]
public class AttendanceController : ErpControllerBase
{
    private readonly IAttendanceService _attendance;
    public AttendanceController(IAttendanceService attendance) => _attendance = attendance;

    [HttpGet("day")]
    public async Task<IActionResult> Day([FromQuery] DateTime date, CancellationToken ct) => Success(await _attendance.GetDayAsync(date, ct));

    [HttpPut("day")]
    public async Task<IActionResult> SaveDay([FromBody] SaveAttendanceDayDto request, CancellationToken ct)
        => Success(await _attendance.SaveDayAsync(request, ct), Messages.UpdatedSuccessfully);

    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] List<AttendanceImportRowDto> rows, CancellationToken ct) => Success(await _attendance.ImportAsync(rows, ct));

    /// <summary>ملخص الشهر (period = yyyy-MM) لكل موظف له سجلات، مع أثره المتوقع على المسير.</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] string period, CancellationToken ct) => Success(await _attendance.GetSummaryAsync(period, ct));
}

/// <summary>مسير الرواتب الشهري: معاينة، مسودة واعتماد، ترحيل، سجل، عكس، وملف حماية الأجور.</summary>
[Route("api/v1/payroll"), RequireScreen("hr"), RequireModule(PlatformModules.Hr)]
public class PayrollController : ErpControllerBase
{
    private readonly IPayrollService _payroll;
    public PayrollController(IPayrollService payroll) => _payroll = payroll;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Success(await _payroll.ListAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _payroll.GetAsync(id, ct));

    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] PayrollRunRequestDto request, CancellationToken ct) => Success(await _payroll.PreviewAsync(request, ct));

    /// <summary>يحفظ مسير الشهر مسودة للمراجعة (أو يعيد حساب مسودته).</summary>
    [HttpPost("draft")]
    public async Task<IActionResult> SaveDraft([FromBody] PayrollRunRequestDto request, CancellationToken ct)
        => Success(await _payroll.SaveDraftAsync(request, ct), Messages.PayrollDraftSaved);

    [HttpPost("{id:guid}/approve"), RequireScreen("hr", ScreenAction.Approve)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct) => Success(await _payroll.ApproveAsync(id, ct), Messages.PayrollPosted);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteDraft(Guid id, CancellationToken ct)
    {
        await _payroll.DeleteDraftAsync(id, ct);
        return Success(Messages.DeletedSuccessfully);
    }

    /// <summary>ملف رواتب مسير مرحّل لرفعه للبنك (حماية الأجور).</summary>
    [HttpGet("{id:guid}/wagefile")]
    public async Task<IActionResult> WageFile(Guid id, CancellationToken ct) => Success(await _payroll.GetWageFileAsync(id, ct));

    [HttpPost("post")]
    public async Task<IActionResult> Post([FromBody] PayrollRunRequestDto request, CancellationToken ct)
        => Success(await _payroll.PostAsync(request, ct), Messages.PayrollPosted);

    [HttpPost("{id:guid}/reverse")]
    public async Task<IActionResult> Reverse(Guid id, CancellationToken ct) => Success(await _payroll.ReverseAsync(id, ct), Messages.PayrollReversed);
}
