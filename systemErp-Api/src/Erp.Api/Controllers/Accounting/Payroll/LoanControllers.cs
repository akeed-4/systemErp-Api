using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>سلف الموظفين: صرف بقيد محاسبي، والأقساط تُخصم من مسير الرواتب تلقائياً.</summary>
[Route("api/v1/employeeloans"), RequireScreen("hr")]
public class EmployeeLoansController : ErpControllerBase
{
    private readonly IEmployeeLoanService _loans;
    public EmployeeLoansController(IEmployeeLoanService loans) => _loans = loans;

    /// <summary>القائمة بخيارات DevExtreme وتُعيد <c>LoadResult</c> مباشرة دون مغلّف ApiResponse.</summary>
    [HttpGet("load")]
    public async Task<IActionResult> Load(DataSourceLoadOptions loadOptions, CancellationToken ct) => Ok(await _loans.LoadAsync(loadOptions, ct));

    /// <summary>السلفة مع سجل سدادها.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _loans.GetAsync(id, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateEmployeeLoanDto dto, CancellationToken ct)
    {
        var created = await _loans.CreateAsync(dto, ct);
        return Created($"{Request.Path}/{created.Id}", ApiResponse<EmployeeLoanDto>.Ok(created, Messages.CreatedSuccessfully).WithStatus(201));
    }

    [HttpPut("{id:guid}/schedule")]
    public async Task<IActionResult> Reschedule(Guid id, [FromBody] RescheduleEmployeeLoanDto dto, CancellationToken ct)
        => Success(await _loans.RescheduleAsync(id, dto, ct), Messages.UpdatedSuccessfully);

    [HttpPost("{id:guid}/cancel"), RequireScreen("hr", ScreenAction.Delete)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) => Success(await _loans.CancelAsync(id, ct), Messages.LoanCancelled);
}

/// <summary>تصفية نهاية الخدمة: معاينة الحساب، ترحيله وإنهاء خدمة الموظف، وعكسه (بصلاحية الاعتماد).</summary>
[Route("api/v1/endofservice"), RequireScreen("hr")]
public class EndOfServiceController : ErpControllerBase
{
    private readonly IEndOfServiceService _service;
    public EndOfServiceController(IEndOfServiceService service) => _service = service;

    [HttpGet("load")]
    public async Task<IActionResult> Load(DataSourceLoadOptions loadOptions, CancellationToken ct) => Ok(await _service.LoadAsync(loadOptions, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _service.GetAsync(id, ct));

    [HttpPost("preview"), RequireScreen("hr", ScreenAction.View)]
    public async Task<IActionResult> Preview([FromBody] EndOfServiceRequestDto request, CancellationToken ct) => Success(await _service.PreviewAsync(request, ct));

    [HttpPost, RequireScreen("hr", ScreenAction.Approve)]
    public async Task<IActionResult> Post([FromBody] EndOfServiceRequestDto request, CancellationToken ct)
        => Success(await _service.PostAsync(request, ct), Messages.EndOfServicePosted);

    [HttpPost("{id:guid}/reverse"), RequireScreen("hr", ScreenAction.Approve)]
    public async Task<IActionResult> Reverse(Guid id, CancellationToken ct) => Success(await _service.ReverseAsync(id, ct), Messages.EndOfServiceReversed);
}
