using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>
/// خطة تنفيذ المشروع من دراسة الجدوى: القراءة بعرض العقد، والتوليد والتعديل والتقديم بتعديله، والاعتماد والإعادة والمراجعة
/// بصلاحية الاعتماد، والاعتماد يمر بسياسة الاعتماد (<c>contract-execution-plan</c>) إن وُجدت.
/// </summary>
[Route("api/v1/commercialcontracts/{id:guid}/execution-plan"), RequireScreen(PermissionCatalog.CommercialContractsScreenId)]
[RequireModule(PlatformModules.Construction)]
[ScopedRecord(ScopedRecordTypes.CommercialContractKey)]
[RequireFeature(PlanFeatures.CommercialCycle)]
public class ExecutionPlanController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.CommercialContractsScreenId;
    private const string Record = ScopedRecordTypes.CommercialContractKey;
    private const string Approval = "contract-execution-plan";
    private readonly IExecutionPlanService _plans;
    private readonly IPermissionService _permissions;

    public ExecutionPlanController(IExecutionPlanService plans, IPermissionService permissions)
    {
        _plans = plans; _permissions = permissions;
    }

    [HttpGet, RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _plans.GetAsync(id, ct));

    /// <summary>الدراسة ↔ الأساس المعتمد ↔ الخطة الحالية ↔ الفعلي من المستندات المعتمدة.</summary>
    [HttpGet("comparison"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Comparison(Guid id, CancellationToken ct) => Success(await _plans.ComparisonAsync(id, ct));

    /// <summary>توليد الخطة من الدراسة المجمّدة (المشتق يُستبدل، واليدوي والمعدّل يبقيان).</summary>
    [HttpPost("generate"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> Generate(Guid id, [FromBody] GenerateExecutionPlanDto request, CancellationToken ct)
    {
        return Success(await _plans.GenerateAsync(id, request, ct));
    }

    [HttpPut("resources"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> SaveResources(Guid id, [FromBody] SaveResourceRequirementsDto request, CancellationToken ct)
    {
        return Success(await _plans.SaveResourcesAsync(id, request, ct));
    }

    [HttpPost("submit"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct)
    {
        return Success(await _plans.SubmitAsync(id, ct));
    }

    /// <summary>الاعتماد: يُحجز بطلب اعتماد إن وُجدت سياسة، ويجمّد نسختي الدراسة والأساس ويعتمد المواد معه.</summary>
    [HttpPost("approve"), RequireScreen(Screen, ScreenAction.Approve)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        if (await HoldForApprovalAsync(Approval, ApprovalDocumentCatalog.Approve, id, null, ct) is { } held) return held;
        return Success(await _plans.ApproveAsync(id, ct));
    }

    [HttpPost("return"), RequireScreen(Screen, ScreenAction.Approve)]
    public async Task<IActionResult> Return(Guid id, [FromBody] ReturnExecutionPlanDto request, CancellationToken ct)
    {
        return Success(await _plans.ReturnAsync(id, request, ct));
    }

    /// <summary>مراجعة الخطة المعتمدة (مسودة برقم أعلى؛ النسخ المعتمدة تبقى).</summary>
    [HttpPost("revise"), RequireScreen(Screen, ScreenAction.Approve)]
    public async Task<IActionResult> Revise(Guid id, CancellationToken ct)
    {
        return Success(await _plans.ReviseAsync(id, ct));
    }
}
