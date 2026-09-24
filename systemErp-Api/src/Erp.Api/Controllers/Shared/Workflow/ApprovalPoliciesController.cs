using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

[Route("api/v1/approval-policies")]
[RequireScreen("approval-policies")]
public class ApprovalPoliciesController : CrudController<ApprovalPolicyDto, CreateApprovalPolicyDto, UpdateApprovalPolicyDto>
{
    private readonly IApprovalPolicyService _policies;
    public ApprovalPoliciesController(IApprovalPolicyService policies) : base(policies) => _policies = policies;

    [HttpPatch("{id:guid}/active")]
    public async Task<IActionResult> SetActive(Guid id, [FromQuery] bool isActive, CancellationToken ct)
        => Success(await _policies.SetActiveAsync(id, isActive, ct));
}
