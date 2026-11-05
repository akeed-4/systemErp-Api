using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

/// <summary>
/// كتالوج دورة العقود العام (حالات العقد وانتقالاتها لكل المنشآت): لمدير المنصة وحده، لأن التعديل يسري على جميع المنشآت.
/// </summary>
[Route("api/v1/platform/contract-workflow"), RequirePlatformAdmin]
public class ContractWorkflowCatalogController : ErpControllerBase
{
    private readonly IContractWorkflowCatalogService _catalog;

    public ContractWorkflowCatalogController(IContractWorkflowCatalogService catalog) => _catalog = catalog;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Success(await _catalog.GetAsync(ct));

    [HttpPut("stages/{stageId}")]
    public async Task<IActionResult> UpdateStage(string stageId, [FromBody] SaveContractStageDto dto, CancellationToken ct)
        => Success(await _catalog.UpdateStageAsync(stageId, dto, ct), Messages.UpdatedSuccessfully);

    [HttpPost("transitions")]
    public async Task<IActionResult> CreateTransition([FromBody] SaveContractTransitionDto dto, CancellationToken ct)
        => Success(await _catalog.CreateTransitionAsync(dto, ct), Messages.CreatedSuccessfully);

    [HttpPut("transitions/{transitionId:int}")]
    public async Task<IActionResult> UpdateTransition(int transitionId, [FromBody] SaveContractTransitionDto dto, CancellationToken ct)
        => Success(await _catalog.UpdateTransitionAsync(transitionId, dto, ct), Messages.UpdatedSuccessfully);
}
