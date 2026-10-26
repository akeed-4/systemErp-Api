using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace ERP.Api.Controllers.Shared;

/// <summary>
/// مركز الصلاحيات: الكتالوج، صلاحيات الأدوار والمستخدمين ونطاقهم وحد اعتمادهم، الصلاحيات الفعلية، فحص الصلاحية،
/// إسناد السجلات، وسجل التغييرات. القراءة بصلاحية عرض الشاشة والتعديل بصلاحية تعديلها.
/// </summary>
[Route("api/v1/permissions")]
[RequireScreen("user-permissions")]
public class PermissionsController : ErpControllerBase
{
    private readonly IPermissionService _permissions;
    public PermissionsController(IPermissionService permissions) => _permissions = permissions;

    [HttpGet("screens"), AllowAnonymous]
    public IActionResult Screens() => Success(_permissions.GetScreens());

    /// <summary>الوحدات والشاشات والإجراءات ومسارات الواجهة ونقاط الـ API المحمية بكل إجراء. لأي مستخدم مسجّل (حارس المسارات).</summary>
    [HttpGet("catalog"), AuthenticatedOnly]
    public IActionResult Catalog([FromServices] IActionDescriptorCollectionProvider actions)
    {
        var catalog = _permissions.GetCatalog();
        var endpoints = EndpointsByScreen(actions);
        foreach (var screen in catalog.Modules.SelectMany(m => m.Screens))
            screen.Endpoints = endpoints.GetValueOrDefault(screen.Id) ?? new List<PermissionEndpointDto>();
        return Success(catalog);
    }

    /// <summary>صلاحيات المستخدم الحالي (للواجهة لإخفاء الشاشات والأزرار) - متاحة لأي مستخدم مسجّل.</summary>
    [HttpGet("me"), AuthenticatedOnly]
    public async Task<IActionResult> Mine([FromServices] ICurrentUser me, CancellationToken ct)
        => Success(me.UserId == null || me.RoleId == ERP.Service.Services.Shared.PermissionService.EmployeeRoleId
            ? new List<ScreenPermissionDto>()
            : await _permissions.GetForUserOrRoleAsync(me.UserId, null, ct));

    /// <summary>نطاق وحد اعتماد المستخدم الحالي (للواجهة).</summary>
    [HttpGet("me/settings"), AuthenticatedOnly]
    public async Task<IActionResult> MySettings([FromServices] ICurrentUser me, CancellationToken ct)
        => Success(me.UserId == null ? new PermissionSettingsDto() : await _permissions.GetSettingsAsync(me.UserId, null, ct));

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] Guid? userId, [FromQuery] string? roleId, CancellationToken ct)
        => Success(await _permissions.GetForEditingAsync(userId, roleId, ct));

    [HttpGet("settings")]
    public async Task<IActionResult> Settings([FromQuery] Guid? userId, [FromQuery] string? roleId, CancellationToken ct)
        => Success(await _permissions.GetSettingsAsync(userId, roleId, ct));

    [HttpPost, RequireScreen("user-permissions", ScreenAction.Edit)]
    public async Task<IActionResult> Save([FromBody] SavePermissionsRequestDto request, CancellationToken ct)
    {
        await _permissions.SaveAsync(request, ct);
        return Success(Messages.PermissionsSaved);
    }

    /// <summary>حذف التخصيص المباشر للمستخدم فيعود لصلاحيات دوره.</summary>
    [HttpDelete("users/{userId:guid}"), RequireScreen("user-permissions", ScreenAction.Edit)]
    public async Task<IActionResult> ResetUser(Guid userId, CancellationToken ct)
    {
        await _permissions.ResetUserAsync(userId, ct);
        return Success(Messages.PermissionsResetToRole);
    }

    [HttpGet("customized-users")]
    public async Task<IActionResult> CustomizedUsers(CancellationToken ct)
        => Success(await _permissions.GetCustomizedUsersAsync(ct));

    [HttpGet("effective/{userId:guid}")]
    public async Task<IActionResult> Effective(Guid userId, CancellationToken ct)
        => Success(await _permissions.GetEffectiveAsync(userId, ct));

    /// <summary>فحص صلاحية: المستخدم + العملية + السجل ← مسموح/غير مسموح وسبب الرفض. قراءة فقط.</summary>
    [HttpPost("check"), RequireScreen("user-permissions", ScreenAction.View)]
    public async Task<IActionResult> Check([FromBody] AuthorizationCheckRequestDto request, CancellationToken ct)
        => Success(await _permissions.CheckAsync(request, ct));

    [HttpGet("records")]
    public async Task<IActionResult> Records([FromQuery] string type, [FromQuery] string? term, CancellationToken ct)
        => Success(await _permissions.SearchRecordsAsync(type, term, ct));

    [HttpPost("records/assign"), RequireScreen("user-permissions", ScreenAction.Edit)]
    public async Task<IActionResult> Assign([FromBody] AssignRecordDto request, CancellationToken ct)
    {
        await _permissions.AssignRecordAsync(request, ct);
        return Success(Messages.RecordAssigned);
    }

    [HttpGet("changes")]
    public async Task<IActionResult> Changes([FromQuery] PaginationParams p, CancellationToken ct)
        => Success(await _permissions.GetChangeLogAsync(p, ct));

    /// <summary>نقاط الـ API لكل شاشة وإجراء كما تحميها سمات RequireScreen فعلاً (سمة الإجراء تتقدّم على سمة الـ controller).</summary>
    private static Dictionary<string, List<PermissionEndpointDto>> EndpointsByScreen(IActionDescriptorCollectionProvider provider)
    {
        var result = new Dictionary<string, List<PermissionEndpointDto>>();
        foreach (var descriptor in provider.ActionDescriptors.Items.OfType<ControllerActionDescriptor>())
        {
            var metadata = descriptor.EndpointMetadata;
            if (metadata.OfType<IAllowAnonymous>().Any() || metadata.OfType<AuthenticatedOnlyAttribute>().Any()) continue;
            var screen = metadata.OfType<RequireScreenAttribute>().LastOrDefault();
            if (screen == null) continue;
            var methods = descriptor.ActionConstraints?.OfType<Microsoft.AspNetCore.Mvc.ActionConstraints.HttpMethodActionConstraint>()
                .SelectMany(c => c.HttpMethods).ToList() ?? new List<string>();
            foreach (var method in methods.DefaultIfEmpty("GET"))
            {
                var action = screen.Action ?? RequireScreenAttribute.FromMethod(method);
                if (!result.TryGetValue(screen.ScreenId, out var list)) result[screen.ScreenId] = list = new();
                list.Add(new PermissionEndpointDto
                {
                    Action = ERP.Service.Services.Shared.PermissionCatalog.Code(action), Method = method,
                    Route = "/" + (descriptor.AttributeRouteInfo?.Template ?? string.Empty),
                });
            }
        }
        foreach (var list in result.Values) list.Sort((a, b) => string.CompareOrdinal(a.Route + a.Method, b.Route + b.Method));
        return result;
    }
}
