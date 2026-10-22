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

    /// <summary>كتالوج الوحدات المعتمدة، يُستخدم لعرض تعريف موحّد للوحدات في الواجهة.</summary>
    [HttpGet("modules")]
    public async Task<IActionResult> Modules(CancellationToken ct)
        => Success(await _platform.ListModulesAsync(ct));

    [HttpGet("summary"), RequirePlatformAdmin]
    public async Task<IActionResult> Summary(CancellationToken ct) => Success(await _platform.SummaryAsync(ct));

    [HttpGet("tenants"), RequirePlatformAdmin]
    public async Task<IActionResult> Tenants(CancellationToken ct) => Success(await _platform.ListTenantsAsync(ct));

    [HttpGet("settings"), RequirePlatformAdmin]
    public async Task<IActionResult> Settings(CancellationToken ct) => Success(await _platform.GetSettingsAsync(ct));

    /// <summary>نوع قاعدة المنشآت الجديدة من التسجيل العام (لمدير المنصة بصلاحية الإدارة)؛ لا يغيّر قاعدة منشأة قائمة.</summary>
    [HttpPut("settings"), RequirePlatformAdmin]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdatePlatformSettingsDto request, CancellationToken ct)
        => Success(await _platform.UpdateSettingsAsync(request, ct), Messages.PlatformSettingsSaved);

    /// <summary>حساب Paymob الخاص بالمنصة لدفع اشتراكات المنشآت (الأسرار لا تُعاد).</summary>
    [HttpGet("payment-gateway"), RequirePlatformAdmin]
    public async Task<IActionResult> PaymentGateway(CancellationToken ct) => Success(await _platform.GetPaymobSettingsAsync(ct));

    [HttpPut("payment-gateway"), RequirePlatformAdmin]
    public async Task<IActionResult> UpdatePaymentGateway([FromBody] UpdatePlatformPaymobSettingsDto request, CancellationToken ct)
        => Success(await _platform.UpdatePaymobSettingsAsync(request, ct), Messages.PlatformSettingsSaved);

    /// <summary>منشأة جديدة على القاعدة المشتركة أو بقاعدة مستقلة: هذا المسار وحده يقبل نوع القاعدة (لمدير المنصة بصلاحية الإدارة).</summary>
    [HttpPost("tenants"), RequirePlatformAdmin]
    public async Task<IActionResult> CreateTenant([FromBody] PlatformCreateTenantRequestDto request, CancellationToken ct)
        => Success(await _platform.CreateTenantAsync(request, ct), Messages.TenantCreated);

    /// <summary>إعادة تجهيز قاعدة منشأة مستقلة متعثّرة؛ لا يحوّل منشأة مشتركة ولا ينقل بياناتها.</summary>
    [HttpPost("tenants/{tenantId:guid}/database/provision"), RequirePlatformAdmin]
    public async Task<IActionResult> ProvisionDatabase(Guid tenantId, CancellationToken ct)
        => Success(await _platform.ProvisionDatabaseAsync(tenantId, ct), Messages.TenantDatabaseProvisioned);

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

    [HttpPost("plans"), RequirePlatformAdmin]
    public async Task<IActionResult> CreatePlan([FromBody] CreatePlanRequestDto request, CancellationToken ct)
        => Success(await _platform.CreatePlanAsync(request, ct), "تم إنشاء الباقة");

    [HttpPut("plans/{id}"), RequirePlatformAdmin]
    public async Task<IActionResult> UpdatePlan(string id, [FromBody] UpdatePlanRequestDto request, CancellationToken ct)
        => Success(await _platform.UpdatePlanAsync(id, request, ct), "تم حفظ الباقة");

    [HttpDelete("plans/{id}"), RequirePlatformAdmin]
    public async Task<IActionResult> DeletePlan(string id, CancellationToken ct)
    {
        await _platform.DeletePlanAsync(id, ct);
        return Success("تم حذف الباقة");
    }
}
