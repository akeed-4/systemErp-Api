using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Purchasing;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>
/// دورة الشراء: طلب عروض الأسعار من طلب شراء معتمد، وعروض الموردين ومقارنتها، والترسية (صلاحية مستقلة) إلى أوامر شراء،
/// والاستلام الجزئي على الأمر وفوترة كل سند استلام.
/// </summary>
[Route("api/v1/purchase-cycle"), RequireScreen("purchases")]
[RequireModule(PlatformModules.Accounting)]
[RequireFeature(PlanFeatures.CommercialCycle)]
public class PurchaseCycleController : ErpControllerBase
{
    private const string Award = PermissionCatalog.PurchaseQuotationAwardScreenId;
    /// <summary>معالج الترسية في <see cref="ApprovalRegistry"/>.</summary>
    public const string AwardApproval = "purchase-rfq-award";
    private readonly IPurchaseCycleService _cycle;
    public PurchaseCycleController(IPurchaseCycleService cycle) => _cycle = cycle;

    [HttpGet("rfqs")]
    public async Task<IActionResult> List([FromQuery] Guid? requisitionId, [FromQuery] string? status, CancellationToken ct)
        => Success(await _cycle.ListRfqsAsync(requisitionId, status, ct));

    [HttpGet("rfqs/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _cycle.GetRfqAsync(id, ct));

    [HttpPost("rfqs"), RequireScreen("purchases", ScreenAction.Create)]
    public async Task<IActionResult> Create([FromBody] CreatePurchaseRfqDto request, CancellationToken ct)
        => Success(await _cycle.CreateRfqAsync(request, ct));

    [HttpPost("rfqs/{id:guid}/suppliers"), RequireScreen("purchases", ScreenAction.Edit)]
    public async Task<IActionResult> AddSuppliers(Guid id, [FromBody] AddRfqSuppliersDto request, CancellationToken ct)
        => Success(await _cycle.AddSuppliersAsync(id, request, ct));

    [HttpPost("rfqs/{id:guid}/quotations"), RequireScreen("purchases", ScreenAction.Create)]
    public async Task<IActionResult> SubmitQuotation(Guid id, [FromBody] SubmitSupplierQuotationDto request, CancellationToken ct)
        => Success(await _cycle.SubmitQuotationAsync(id, request, ct));

    [HttpGet("rfqs/{id:guid}/comparison")]
    public async Task<IActionResult> Compare(Guid id, CancellationToken ct) => Success(await _cycle.CompareAsync(id, ct));

    /// <summary>الترسية: صلاحية شاشة الترسية أولاً، ثم سياسة الاعتماد إن وُجدت (202 بطلب اعتماد يُنفَّذ عند الاعتماد النهائي).</summary>
    [HttpPost("rfqs/{id:guid}/award"), RequireScreen(Award, ScreenAction.Approve)]
    public async Task<IActionResult> AwardRfq(Guid id, [FromBody] AwardRfqDto request, CancellationToken ct)
    {
        if (await HoldForApprovalAsync(AwardApproval, ApprovalDocumentCatalog.Approve, id, request, ct) is { } held) return held;
        return Success(await _cycle.AwardAsync(id, request, ct));
    }

    [HttpPost("rfqs/{id:guid}/cancel"), RequireScreen(Award, ScreenAction.Approve)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) => Success(await _cycle.CancelRfqAsync(id, ct));

    [HttpGet("orders")]
    public async Task<IActionResult> Orders([FromQuery] string? status, [FromQuery] Guid? requisitionId, [FromQuery] Guid? rfqId, CancellationToken ct)
        => Success(await _cycle.ListOrdersAsync(status, requisitionId, rfqId, ct));

    [HttpGet("orders/{id:guid}")]
    public async Task<IActionResult> Order(Guid id, CancellationToken ct) => Success(await _cycle.OrderTrackingAsync(id, ct));

    [HttpPost("orders/{id:guid}/receipts"), RequireScreen("purchases", ScreenAction.Create)]
    public async Task<IActionResult> Receive(Guid id, [FromBody] ReceivePurchaseOrderDto request, CancellationToken ct)
        => Success(await _cycle.ReceiveAsync(id, request, ct));

    [HttpPost("orders/{id:guid}/receipts/{noteId:guid}/invoice"), RequireScreen("purchases", ScreenAction.Create)]
    public async Task<IActionResult> Invoice(Guid id, Guid noteId, [FromQuery] DateTime? date, CancellationToken ct)
        => Success(await _cycle.InvoiceReceiptAsync(id, noteId, date, ct));

    [HttpPost("orders/{id:guid}/invoice"), RequireScreen("purchases", ScreenAction.Create)]
    public async Task<IActionResult> InvoiceReceipts(Guid id, [FromBody] InvoicePurchaseReceiptsDto request, CancellationToken ct)
        => Success(await _cycle.InvoiceReceiptsAsync(id, request, ct));
}
