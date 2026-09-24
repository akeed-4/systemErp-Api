using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

[Route("api/v1/commercialcontracts"), RequireScreen("sales")]
public class CommercialContractsController : CrudController<CommercialContractDto, CreateCommercialContractDto, UpdateCommercialContractDto>
{
    private readonly ICommercialContractService _contracts;
    public CommercialContractsController(ICommercialContractService s) : base(s) => _contracts = s;

    [HttpPost("{id:guid}/advance-stage")]
    public async Task<IActionResult> Advance(Guid id, [FromBody] AdvanceStageRequestDto dto, CancellationToken ct)
        => Success(await _contracts.AdvanceStageAsync(id, dto, ct), "تم نقل العقد للمرحلة التالية");

    [HttpPost("{id:guid}/milestones/{milestoneId:guid}/bill")]
    public async Task<IActionResult> Bill(Guid id, Guid milestoneId, CancellationToken ct)
        => Success(await _contracts.BillMilestoneAsync(id, milestoneId, ct), "تمت فوترة المستخلص");
}
