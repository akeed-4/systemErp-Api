using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

/// <summary>قراءة سجل التدقيق فقط — لا يوجد Endpoint لإنشاء أو تعديل أو حذف سجلات التدقيق (تُكتب داخليًا فقط عبر IAuditService.LogAsync).</summary>
[Route("api/v1/auditlogs"), RequireScreen("car-showroom")]
public class AuditLogsController : ErpControllerBase
{
    private readonly IAuditService _audit;
    public AuditLogsController(IAuditService audit) => _audit = audit;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string entityName, [FromQuery] string? entityId, [FromQuery] PaginationParams q, CancellationToken ct)
    {
        q.Status = entityName;
        q.SearchTerm = entityId;
        q.IsDescending = true;
        return Success(await _audit.ListAsync(q, ct));
    }
}
