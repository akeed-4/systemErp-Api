using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>
/// سند التكاليف الإضافية للمشتريات العامة: يحمّل الشحن والجمارك والتخليص على تكلفة أصناف فواتير الشراء المرحّلة.
/// الاعتماد والإلغاء صلاحيتان مستقلتان على الشاشة نفسها.
/// </summary>
[Route("api/v1/purchase-landed-costs"), RequireScreen("purchase-landed-costs")]
[RequireModule(PlatformModules.Accounting)]
[RequireFeature(PlanFeatures.LandedCosts)]
public class PurchaseLandedCostsController : ErpControllerBase
{
    private readonly IPurchaseLandedCostService _service;
    public PurchaseLandedCostsController(IPurchaseLandedCostService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PaginationParams q, CancellationToken ct) => Success(await _service.ListAsync(q, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _service.GetAsync(id, ct));

    /// <summary>معاينة التوزيع وأثره على تكلفة الأصناف قبل الحفظ أو الاعتماد. لا يُحفظ شيء.</summary>
    [HttpPost("preview"), RequireScreen("purchase-landed-costs", ScreenAction.View)]
    public async Task<IActionResult> Preview([FromBody] SavePurchaseLandedCostDto dto, CancellationToken ct) => Success(await _service.PreviewAsync(dto, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SavePurchaseLandedCostDto dto, CancellationToken ct)
    {
        if (await HoldForApprovalAsync(Approval, ApprovalDocumentCatalog.Create, null, dto, ct) is { } held) return held;
        var created = await _service.CreateAsync(dto, ct);
        return Created($"{Request.Path}/{created.Id}", ApiResponse<PurchaseLandedCostDto>.Ok(created, Messages.CreatedSuccessfully).WithStatus(201));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SavePurchaseLandedCostDto dto, CancellationToken ct)
    {
        if (await HoldForApprovalAsync(Approval, ApprovalDocumentCatalog.Edit, id, dto, ct) is { } held) return held;
        return Success(await _service.UpdateAsync(id, dto, ct), Messages.UpdatedSuccessfully);
    }

    [HttpPost("{id:guid}/approve"), RequireScreen("purchase-landed-costs", ScreenAction.Approve)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        if (await HoldForApprovalAsync(Approval, ApprovalDocumentCatalog.Approve, id, null, ct) is { } held) return held;
        return Success(await _service.ApproveAsync(id, ct), Messages.PurchaseLandedCostApproved);
    }

    [HttpPost("{id:guid}/cancel"), RequireScreen("purchase-landed-costs", ScreenAction.Cancel)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        if (await HoldForApprovalAsync(Approval, ApprovalDocumentCatalog.Cancel, id, null, ct) is { } held) return held;
        return Success(await _service.CancelAsync(id, ct), Messages.PurchaseLandedCostCancelled);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (await HoldForApprovalAsync(Approval, ApprovalDocumentCatalog.Delete, id, null, ct) is { } held) return held;
        await _service.DeleteAsync(id, ct);
        return Success(Messages.DeletedSuccessfully);
    }

    // تكاليف الشراء الإضافية تخضع لسياسات الاعتماد: العملية المنطبقة تُحجز وتُنفَّذ عند اعتمادها
    private const string Approval = "purchase-landed-costs";
}

/// <summary>أنواع التكاليف الإضافية للمشتريات: تُعرض لمن يرى الشاشة، وتُدار بصلاحية التعديل.</summary>
[Route("api/v1/purchase-expense-types"), RequireScreen("purchase-landed-costs")]
[RequireModule(PlatformModules.Accounting)]
[RequireFeature(PlanFeatures.LandedCosts)]
public class PurchaseExpenseTypesController : ErpControllerBase
{
    private readonly IPurchaseExpenseTypeService _service;
    public PurchaseExpenseTypesController(IPurchaseExpenseTypeService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken ct) => Success(await _service.ListAsync(includeInactive, ct));

    [HttpPost, RequireScreen("purchase-landed-costs", ScreenAction.Edit)]
    public async Task<IActionResult> Create([FromBody] SavePurchaseExpenseTypeDto dto, CancellationToken ct)
    {
        var created = await _service.CreateAsync(dto, ct);
        return Created($"{Request.Path}/{created.Id}", ApiResponse<PurchaseExpenseTypeDto>.Ok(created, Messages.CreatedSuccessfully).WithStatus(201));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SavePurchaseExpenseTypeDto dto, CancellationToken ct)
        => Success(await _service.UpdateAsync(id, dto, ct), Messages.UpdatedSuccessfully);
}
