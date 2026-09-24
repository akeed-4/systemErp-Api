using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

[Route("api/v1/materialrequisitions"), RequireScreen("purchases")]
public class MaterialRequisitionsController : CrudController<MaterialRequisitionDto, CreateMaterialRequisitionDto, UpdateMaterialRequisitionDto>
{
    private readonly IMaterialRequisitionService _requisitions;
    public MaterialRequisitionsController(IMaterialRequisitionService s) : base(s) => _requisitions = s;

    [HttpPost("{id:guid}/approve"), RequireScreen("purchases", ScreenAction.Approve)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct) => Success(await _requisitions.ApproveAsync(id, ct), "تم اعتماد الطلب");

    [HttpPost("{id:guid}/convert-to-invoice")]
    public async Task<IActionResult> ToInvoice(Guid id, CancellationToken ct) => Success(await _requisitions.ConvertToPurchaseInvoiceAsync(id, ct), "تم إنشاء فاتورة شراء مسودة");
}
