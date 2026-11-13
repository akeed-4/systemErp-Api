using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

[Route("api/v1/materialrequisitions"), RequireScreen("purchases")]
[RequireModule(PlatformModules.Accounting)]
[ScopedRecord("material-requisition")]
[RequireFeature(PlanFeatures.CommercialCycle)]
public class MaterialRequisitionsController : CrudController<MaterialRequisitionDto, CreateMaterialRequisitionDto, UpdateMaterialRequisitionDto>
{
    private readonly IMaterialRequisitionService _requisitions;
    private readonly IPermissionService _permissions;
    public MaterialRequisitionsController(IMaterialRequisitionService s, IPermissionService permissions) : base(s)
    {
        _requisitions = s; _permissions = permissions;
    }

    protected override string? ApprovalHandler => "material-requisitions";

    /// <summary>
    /// الاعتماد: صلاحية «اعتماد» على المشتريات بنطاقها وحد اعتماد المستخدم مقابل التكلفة التقديرية، ثم سياسة الاعتماد إن وُجدت
    /// (يُحجز الطلب بمسار مستوياتها ولا يُعتمد إلا عند الاعتماد النهائي).
    /// </summary>
    [HttpPost("{id:guid}/approve"), RequireScreen(PermissionCatalog.PurchaseRequisitionApprovalScreenId, ScreenAction.Approve)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        // صلاحية مستقلة لاعتماد الطلب (ترث «اعتماد» المشتريات حتى تُحفظ)، بنطاقها وحد الاعتماد مقابل التكلفة التقديرية
        await _permissions.AuthorizeRecordAsync(PermissionCatalog.PurchaseRequisitionApprovalScreenId, ScreenAction.Approve, "material-requisition", id, ct: ct);
        if (await HoldForApprovalAsync(ApprovalHandler!, ERP.Core.Models.Shared.ApprovalDocumentCatalog.Approve, id, null, ct) is { } held) return held;
        return Success(await _requisitions.ApproveAsync(id, ct), Messages.RequestApproved);
    }

    [HttpPost("{id:guid}/ConvertToInvoice")]
    public async Task<IActionResult> ToInvoice(Guid id, CancellationToken ct) => Success(await _requisitions.ConvertToPurchaseInvoiceAsync(id, ct), Messages.DraftPurchaseInvoiceCreated);
}
