using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>
/// التكلفة المعيارية لساعة الموظف (الفني في الورشة، المشغّل في التأجير). يكفي أحد الإضافتين للمسار، والخدمة تفرض إضافة الغرض المطلوب
/// وصلاحية شاشة إعداداتها.
/// </summary>
[Route("api/v1/employee-cost-rates"), RequireModule(PlatformModules.Workshop, PlatformModules.EquipmentRental)]
public class EmployeeCostRatesController : ErpControllerBase
{
    private readonly IEmployeeCostRateService _rates;
    public EmployeeCostRatesController(IEmployeeCostRateService rates) => _rates = rates;

    /// <summary>سجل الغرض (`technician` أو `operator`)، اختيارياً لموظف واحد.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string purpose, [FromQuery] Guid? employeeId, CancellationToken ct)
        => Success(await _rates.ListAsync(purpose, employeeId, ct));

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] CreateEmployeeCostRateDto request, CancellationToken ct)
        => Success(await _rates.AddAsync(request, ct), Messages.CreatedSuccessfully);

    /// <summary>الفنيون أو المشغّلون (موظفون نشطون) بتكلفتهم الحالية، دون اشتراط إضافة شؤون الموظفين.</summary>
    [HttpGet("staff")]
    public async Task<IActionResult> Staff([FromQuery] string purpose, CancellationToken ct)
        => Success(await _rates.StaffAsync(purpose, ct));

    /// <summary>تسجيل موظف أساسي (فني أو مشغّل) من إعدادات الإضافة عبر خدمة الموظفين القائمة، مع تكلفته المعيارية اختيارياً.</summary>
    [HttpPost("staff")]
    public async Task<IActionResult> RegisterStaff([FromBody] RegisterAddOnStaffDto request, CancellationToken ct)
        => Success(await _rates.RegisterStaffAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _rates.DeleteAsync(id, ct);
        return Success(Messages.DeletedSuccessfully);
    }
}
