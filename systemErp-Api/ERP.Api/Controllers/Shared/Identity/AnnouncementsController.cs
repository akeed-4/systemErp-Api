using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

/// <summary>إعلانات المنصة كما تظهر لمستخدمي المنشأة الحالية (أي مستخدم مسجّل).</summary>
[Route("api/v1/announcements")]
public class AnnouncementsController : ErpControllerBase
{
    private readonly IPlatformAnnouncementService _announcements;
    public AnnouncementsController(IPlatformAnnouncementService announcements) => _announcements = announcements;

    [HttpGet("active")]
    public async Task<IActionResult> Active(CancellationToken ct) => Success(await _announcements.ActiveForCurrentTenantAsync(ct));
}
