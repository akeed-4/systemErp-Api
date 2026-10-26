using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

[Route("api/v1/materialrequisitions"), RequireScreen("purchases")]
[RequireModule(PlatformModules.Accounting)]
[ScopedRecord("material-requisition")]
public class MaterialRequisitionsController : CrudController<MaterialRequisitionDto, CreateMaterialRequisitionDto, UpdateMaterialRequisitionDto>
{
    private readonly IMaterialRequisitionService _requisitions;
    private readonly IPermissionService _permissions;
    public MaterialRequisitionsController(IMaterialRequisitionService s, IPermissionService permissions) : base(s)
    {
        _requisitions = s; _permissions = permissions;
    }

    /// <summary>الاعتماد يفحص حد اعتماد المستخدم مقابل التكلفة التقديرية للطلب.</summary>
    [HttpPost("{id:guid}/approve"), RequireScreen("purchases", ScreenAction.Approve)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync("purchases", ScreenAction.Approve, "material-requisition", id, ct: ct);
        return Success(await _requisitions.ApproveAsync(id, ct), Messages.RequestApproved);
    }

    [HttpPost("{id:guid}/ConvertToInvoice")]
    public async Task<IActionResult> ToInvoice(Guid id, CancellationToken ct) => Success(await _requisitions.ConvertToPurchaseInvoiceAsync(id, ct), Messages.DraftPurchaseInvoiceCreated);
}
