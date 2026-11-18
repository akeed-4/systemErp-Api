using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

[Route("api/v1/approvals")]
public class ApprovalsController : ErpControllerBase
{
    private readonly IApprovalService _approvals;
    private readonly IApprovalDecisionService _decisions;
    private readonly IPermissionService _permissions;
    public ApprovalsController(IApprovalService approvals, IApprovalDecisionService decisions, IPermissionService permissions)
    {
        _approvals = approvals; _decisions = decisions; _permissions = permissions;
    }

    /// <summary>المستندات والعمليات التي تخضع لسياسات الاعتماد، مجمّعة بالوحدات (شاشة تعريف السياسة).</summary>
    [HttpGet("document-types"), RequireScreen("approval-policies", ScreenAction.View)]
    public IActionResult DocumentTypes([FromServices] IApprovalGate gate) => Success(gate.Catalog());

    [HttpPost("check"), RequireScreen("dashboard")]
    public async Task<IActionResult> Check([FromBody] ApprovalCheckRequestDto request, CancellationToken ct)
        => Success(await _approvals.CheckAndCreateAsync(request, ct));

    // الرؤية تُصفّى في الخدمة: مدير المركز ومدير السياسات يرون الكل، وغيرهم ما يخصّهم (مقدّماً أو معتمِداً)
    [HttpGet, AuthenticatedOnly, EmployeeSelfService]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] PaginationParams query, CancellationToken ct)
        => Success(await _approvals.ListRequestsAsync(status, query, ct));

    [HttpGet("{id:guid}"), AuthenticatedOnly, EmployeeSelfService]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _approvals.GetForApproverAsync(id, ct));

    [HttpDelete("{id:guid}"), RequireScreen("dashboard", ScreenAction.View)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) => Success(await _approvals.CancelAsync(id, ct), Messages.RequestWithdrawn);

    /// <summary>الاعتماد لا يتجاوز حد اعتماد المستخدم (مبلغ المستند في الطلب)، والاعتماد النهائي يرحّل المستند.</summary>
    [HttpPost("{id:guid}/approve"), AuthenticatedOnly, EmployeeSelfService]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApprovalDecisionDto decision, CancellationToken ct)
    {
        // حد الاعتماد بمبلغ الطلب؛ الأهلية والحالة يفحصهما حارس القرار (فالطلب المحسوم في الأثناء تعارض 409 لا 404)
        await _permissions.EnsureApprovalLimitAsync((await _approvals.GetAsync(id, ct)).DocumentAmount, ct);
        return Success(await _decisions.ApproveAsync(id, decision, ct));
    }

    [HttpPost("{id:guid}/reject"), AuthenticatedOnly, EmployeeSelfService]
    public async Task<IActionResult> Reject(Guid id, [FromBody] ApprovalDecisionDto decision, CancellationToken ct)
        => Success(await _decisions.RejectAsync(id, decision, ct));

    /// <summary>إعادة الطلب لمقدّمه للتعديل بسبب إلزامي (بقواعد معتمِد المستوى نفسها).</summary>
    [HttpPost("{id:guid}/return"), AuthenticatedOnly, EmployeeSelfService]
    public async Task<IActionResult> Return(Guid id, [FromBody] ApprovalDecisionDto decision, CancellationToken ct)
        => Success(await _decisions.ReturnAsync(id, decision, ct), Messages.ApprovalRequestReturned);
}
