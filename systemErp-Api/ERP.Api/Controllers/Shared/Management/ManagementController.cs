using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

/// <summary>الإدارة: سجل التدقيق العام (شاشة <c>management-audit</c> الحسّاسة، قراءة فقط).</summary>
[Route("api/v1/management")]
public class ManagementController : ErpControllerBase
{
    private readonly IManagementService _management;
    public ManagementController(IManagementService management) => _management = management;

    [HttpGet("audit-log/load"), RequireScreen(PermissionCatalog.ManagementAuditScreenId, ScreenAction.View)]
    public async Task<IActionResult> AuditLoad(DataSourceLoadOptions loadOptions, CancellationToken ct) => Ok(await _management.AuditLoadAsync(loadOptions, ct));
}
