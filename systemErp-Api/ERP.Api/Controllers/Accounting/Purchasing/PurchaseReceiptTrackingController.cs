using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>
/// متابعة توريد المشتريات: شاشة مستقلة تُمنح يدوياً (عرض، فتح متابعة = إضافة، الموعد والملاحظات = تعديل، رفض، إلغاء)؛ الفحص هنا وفي الخدمة.
/// إنشاء سند الاستلام نفسه عبر <c>deliverynotes</c> (من فاتورة مشتريات: <c>sourceType = purchase_invoice</c>).
/// </summary>
[Route("api/v1/purchase-receipt-tracking"), RequireScreen("purchase-receipt-tracking")]
[RequireModule(PlatformModules.Accounting)]
[RequireFeature(PlanFeatures.CommercialCycle)]
public class PurchaseReceiptTrackingController : ErpControllerBase
{
    private readonly IPurchaseReceiptTrackingService _tracking;
    public PurchaseReceiptTrackingController(IPurchaseReceiptTrackingService tracking) => _tracking = tracking;

    [HttpGet] public async Task<IActionResult> List([FromQuery] PurchaseSupplyTrackingQueryDto q, CancellationToken ct) => Success(await _tracking.ListAsync(q, ct));

    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _tracking.GetAsync(id, ct));

    [HttpGet("{id:guid}/events")] public async Task<IActionResult> Events(Guid id, CancellationToken ct) => Success(await _tracking.GetEventsAsync(id, ct));

    [HttpGet("sources/purchase-invoice/{invoiceId:guid}")]
    public async Task<IActionResult> PurchaseInvoiceSource(Guid invoiceId, CancellationToken ct) => Success(await _tracking.GetPurchaseInvoiceSourceAsync(invoiceId, ct));

    [HttpGet("sources/contract/{contractId:guid}")]
    public async Task<IActionResult> ContractSource(Guid contractId, CancellationToken ct) => Success(await _tracking.GetContractSourceAsync(contractId, ct));

    [HttpPost("open"), RequireScreen("purchase-receipt-tracking", ScreenAction.Create)]
    public async Task<IActionResult> Open([FromBody] OpenPurchaseSupplyTrackingDto dto, CancellationToken ct) => Success(await _tracking.OpenAsync(dto, ct));

    [HttpPut("{id:guid}"), RequireScreen("purchase-receipt-tracking", ScreenAction.Edit)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePurchaseSupplyTrackingDto dto, CancellationToken ct) => Success(await _tracking.UpdateAsync(id, dto, ct));

    [HttpPost("{id:guid}/reject"), RequireScreen("purchase-receipt-tracking", ScreenAction.Reject)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] ClosePurchaseSupplyTrackingDto dto, CancellationToken ct) => Success(await _tracking.RejectAsync(id, dto, ct));

    [HttpPost("{id:guid}/cancel"), RequireScreen("purchase-receipt-tracking", ScreenAction.Cancel)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] ClosePurchaseSupplyTrackingDto dto, CancellationToken ct) => Success(await _tracking.CancelAsync(id, dto, ct));
}

/// <summary>تقارير التوريد والاستلام: شاشة مستقلة للقراءة تُمنح يدوياً.</summary>
[Route("api/v1/purchase-receipt-reports"), RequireScreen("purchase-receipt-reports")]
[RequireModule(PlatformModules.Accounting)]
[RequireFeature(PlanFeatures.CommercialCycle)]
public class PurchaseReceiptReportsController : ErpControllerBase
{
    private readonly IPurchaseReceiptTrackingService _tracking;
    public PurchaseReceiptReportsController(IPurchaseReceiptTrackingService tracking) => _tracking = tracking;

    [HttpGet] public async Task<IActionResult> Report([FromQuery] PurchaseSupplyTrackingQueryDto q, CancellationToken ct) => Success(await _tracking.ReportAsync(q, ct));
}
