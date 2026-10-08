using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>ملفات الموظفين، مع تنبيهات انتهاء الوثائق والعقود ومؤشرات شؤون الموظفين.</summary>
[Route("api/v1/employees"), RequireScreen("hr")]
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

[Route("api/v1/departments"), RequireScreen("hr")]
public class DepartmentsController : CrudController<DepartmentDto, CreateDepartmentDto, UpdateDepartmentDto>
{
    public DepartmentsController(IDepartmentService s) : base(s) { }
}

/// <summary>مسير الرواتب الشهري: معاينة، ترحيل، سجل، وعكس.</summary>
[Route("api/v1/payroll"), RequireScreen("hr")]
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

    [HttpPost("post")]
    public async Task<IActionResult> Post([FromBody] PayrollRunRequestDto request, CancellationToken ct)
        => Success(await _payroll.PostAsync(request, ct), Messages.PayrollPosted);

    [HttpPost("{id:guid}/reverse")]
    public async Task<IActionResult> Reverse(Guid id, CancellationToken ct) => Success(await _payroll.ReverseAsync(id, ct), Messages.PayrollReversed);
}
