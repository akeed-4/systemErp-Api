using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

/// <summary>إعداد ترقيم أنواع المستندات (شاشة «ترقيم المستندات»).</summary>
[Route("api/v1/document-numbering"), RequireScreen("document-numbering")]
public class DocumentNumberingController : ErpControllerBase
{
    private readonly IDocumentNumberingService _numbering;
    public DocumentNumberingController(IDocumentNumberingService numbering) => _numbering = numbering;

    /// <summary>كل الأنواع بإعدادها والرقم التالي (للمستودع المحدد عند الترقيم لكل مستودع).</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? warehouseId, CancellationToken ct) => Success(await _numbering.ListAsync(warehouseId, ct));

    [HttpPut("{key}"), RequireScreen("document-numbering", ScreenAction.Edit)]
    public async Task<IActionResult> Update(string key, [FromBody] UpdateDocumentNumberingDto dto, CancellationToken ct)
        => Success(await _numbering.UpdateAsync(key, dto, ct), Messages.UpdatedSuccessfully);
}

/// <summary>أنواع المستندات المضبوطة يدوياً: تقرؤها شاشات المستندات لتُظهر حقل الرقم (لكل مستخدم مسجّل).</summary>
[Route("api/v1/document-numbering-modes")]
public class DocumentNumberingModesController : ErpControllerBase
{
    private readonly IDocumentNumberingService _numbering;
    public DocumentNumberingModesController(IDocumentNumberingService numbering) => _numbering = numbering;

    [HttpGet] public async Task<IActionResult> ManualTypes(CancellationToken ct) => Success(await _numbering.ManualTypesAsync(ct));
}
