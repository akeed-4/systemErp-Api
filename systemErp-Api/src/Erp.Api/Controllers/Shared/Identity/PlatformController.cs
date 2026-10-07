using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

/// <summary>لوحة مدير المنصة: اشتراكات كل المنشآت وكتالوج الباقات. كل الإجراءات (عدا access) لمدير المنصة فقط.</summary>
[Route("api/v1/platform")]
public class PlatformController : ErpControllerBase
{
    private readonly IPlatformSubscriptionService _platform;
    private readonly IPlatformAccessService _access;

    public PlatformController(IPlatformSubscriptionService platform, IPlatformAccessService access)
    {
        _platform = platform;
        _access = access;
    }

    /// <summary>تُسأل من الواجهة لإظهار قسم المنصة؛ متاحة لأي مستخدم مسجّل وتعيد true/false فقط.</summary>
    [HttpGet("access")]
    public async Task<IActionResult> Access(CancellationToken ct) => Success(await _access.GetAsync(ct));

    [HttpGet("summary"), RequirePlatformAdmin]
    public async Task<IActionResult> Summary(CancellationToken ct) => Success(await _platform.SummaryAsync(ct));

    [HttpGet("tenants"), RequirePlatformAdmin]
    public async Task<IActionResult> Tenants(CancellationToken ct) => Success(await _platform.ListTenantsAsync(ct));

    [HttpGet("tenants/{tenantId:guid}/subscriptions"), RequirePlatformAdmin]
    public async Task<IActionResult> History(Guid tenantId, CancellationToken ct) => Success(await _platform.HistoryAsync(tenantId, ct));

    [HttpPost("tenants/{tenantId:guid}/subscriptions/change-plan"), RequirePlatformAdmin]
    public async Task<IActionResult> ChangePlan(Guid tenantId, [FromBody] PlatformChangePlanRequestDto request, CancellationToken ct)
        => Success(await _platform.ChangePlanAsync(tenantId, request, ct), "تم تغيير الباقة");

    [HttpPost("tenants/{tenantId:guid}/subscriptions/extend"), RequirePlatformAdmin]
    public async Task<IActionResult> Extend(Guid tenantId, [FromBody] PlatformExtendRequestDto request, CancellationToken ct)
        => Success(await _platform.ExtendAsync(tenantId, request, ct), "تم تمديد الاشتراك");

    [HttpPost("tenants/{tenantId:guid}/subscriptions/status"), RequirePlatformAdmin]
    public async Task<IActionResult> SetStatus(Guid tenantId, [FromBody] PlatformSetStatusRequestDto request, CancellationToken ct)
        => Success(await _platform.SetStatusAsync(tenantId, request, ct), "تم تحديث حالة الاشتراك");

    [HttpPost("tenants/{tenantId:guid}/subscriptions/modules"), RequirePlatformAdmin]
    public async Task<IActionResult> SetModules(Guid tenantId, [FromBody] PlatformSetModulesRequestDto request, CancellationToken ct)
        => Success(await _platform.SetModulesAsync(tenantId, request, ct), "تم تحديث الوحدات");

    [HttpGet("plans"), RequirePlatformAdmin]
    public async Task<IActionResult> Plans(CancellationToken ct) => Success(await _platform.ListPlansAsync(ct));

    [HttpPut("plans/{id}"), RequirePlatformAdmin]
    public async Task<IActionResult> UpdatePlan(SubscriptionPlanId id, [FromBody] UpdatePlanRequestDto request, CancellationToken ct)
        => Success(await _platform.UpdatePlanAsync(id, request, ct), "تم حفظ الباقة");
}
