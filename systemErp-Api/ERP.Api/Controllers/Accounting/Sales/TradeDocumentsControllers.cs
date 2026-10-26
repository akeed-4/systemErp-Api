using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>
/// مستندات التجارة العامة، لكل نوع مساره وجدوله: /sales-invoices، /purchase-invoices، /sales-returns، /purchase-returns.
/// الصلاحية بشاشة النوع (sales / purchases / sales-returns)، والمنطق في خدمة النوع.
/// </summary>
public abstract class TradeDocumentsControllerBase : ErpControllerBase
{
    private readonly ITradeDocumentService _documents;
    private readonly IPermissionService _permissions;
    private readonly string _screen;

    protected TradeDocumentsControllerBase(ITradeDocumentService documents, IPermissionService permissions, string screen)
    {
        _documents = documents; _permissions = permissions; _screen = screen;
    }

    protected async Task Require(ScreenAction action, CancellationToken ct, ScreenAction? legacy = null)
    {
        if (!await _permissions.HasPermissionAsync(_screen, action, legacy, ct)) throw new ForbiddenException();
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PaginationParams q, CancellationToken ct)
    {
        await Require(ScreenAction.View, ct);
        return Success(await _documents.ListAsync(q, ct));
    }

    /// <summary>القائمة بخيارات DevExtreme (filter/sort/skip/take/totalSummary) وتُعيد <c>LoadResult</c> مباشرة دون مغلّف ApiResponse.</summary>
    [HttpGet("load")]
    public async Task<IActionResult> Load(DataSourceLoadOptions loadOptions, CancellationToken ct)
    {
        await Require(ScreenAction.View, ct);
        return Ok(await _documents.LoadAsync(loadOptions, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var document = await _documents.GetAsync(id, ct);
        await Require(ScreenAction.View, ct);
        return Success(document);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateInvoiceDto dto, CancellationToken ct)
    {
        await Require(ScreenAction.Create, ct);
        var created = await _documents.CreateAsync(dto, ct);
        return Created($"{Request.Path}/{created.Id}", ApiResponse<InvoiceDto>.Ok(created, Messages.InvoiceCreated).WithStatus(201));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateInvoiceDto dto, CancellationToken ct)
    {
        await Require(ScreenAction.Edit, ct);
        return Success(await _documents.UpdateAsync(id, dto, ct), Messages.InvoiceUpdated);
    }

    [HttpPost("{id:guid}/post")]
    public async Task<IActionResult> Post(Guid id, CancellationToken ct)
    {
        await _documents.GetAsync(id, ct); // غير موجود في جدول هذا النوع ← 404
        await Require(ScreenAction.Post, ct, ScreenAction.Edit);
        return Success(await _documents.PostDraftAsync(id, ct), Messages.InvoicePosted);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _documents.GetAsync(id, ct);
        await Require(ScreenAction.Delete, ct);
        await _documents.DeleteAsync(id, ct);
        return Success(Messages.InvoiceDeleted);
    }

    /// <summary>معاينة القيد المحاسبي (مخزون، تكلفة إيرادات، ضريبة، دفع/ذمم) قبل الحفظ أو الترحيل — بلا أي أثر.</summary>
    [HttpPost("PreviewJournal")]
    public async Task<IActionResult> PreviewJournal([FromBody] CreateInvoiceDto dto, CancellationToken ct)
    {
        await Require(ScreenAction.View, ct);
        return Success(await _documents.PreviewJournalAsync(dto, ct));
    }

    [HttpGet("{id:guid}/journal")]
    public async Task<IActionResult> Journal(Guid id, CancellationToken ct)
    {
        await _documents.GetAsync(id, ct);
        await Require(ScreenAction.View, ct);
        return Success(await _documents.GetJournalAsync(id, ct));
    }

    [HttpPost("{id:guid}/SubmitZatca")]
    public async Task<IActionResult> SubmitZatca(Guid id, CancellationToken ct)
    {
        if (!await _permissions.HasPermissionAsync("zatca", ScreenAction.Edit, ct)) throw new ForbiddenException();
        return Success(await _documents.SubmitToZatcaAsync(id, ct));
    }
}

/// <summary>مرتجعات التجارة العامة: إنشاء المرتجع من فاتورة أصلية مرحّلة إضافة إلى عمليات المستند.</summary>
public abstract class TradeReturnsControllerBase : TradeDocumentsControllerBase
{
    private readonly ITradeReturnService _returns;

    protected TradeReturnsControllerBase(ITradeReturnService returns, IPermissionService permissions, string screen)
        : base(returns, permissions, screen) => _returns = returns;

    /// <summary>مرتجع كلي أو جزئي لفاتورة مرحّلة (إشعار دائن/مدين).</summary>
    [HttpPost("from-invoice")]
    public async Task<IActionResult> CreateFromInvoice([FromBody] CreateReturnInvoiceRequestDto dto, CancellationToken ct)
    {
        await Require(ScreenAction.Create, ct);
        return Success(await _returns.CreateFromInvoiceAsync(dto, ct), Messages.ReturnCreated);
    }
}

[Route("api/v1/sales-invoices"), ScopedRecord("sales-invoice")]
[RequireModule(PlatformModules.Accounting, PlatformModules.Pos)]
public class SalesInvoicesController : TradeDocumentsControllerBase
{
    public SalesInvoicesController(ISalesInvoiceService service, IPermissionService permissions) : base(service, permissions, "sales") { }
}

[Route("api/v1/purchase-invoices"), ScopedRecord("purchase-invoice")]
[RequireModule(PlatformModules.Accounting)]
public class PurchaseInvoicesController : TradeDocumentsControllerBase
{
    public PurchaseInvoicesController(IPurchaseInvoiceService service, IPermissionService permissions) : base(service, permissions, "purchases") { }
}

[Route("api/v1/sales-returns"), ScopedRecord("sales-return")]
[RequireModule(PlatformModules.Accounting, PlatformModules.Pos)]
public class SalesReturnsController : TradeReturnsControllerBase
{
    public SalesReturnsController(ISalesReturnService service, IPermissionService permissions) : base(service, permissions, "sales-returns") { }
}

[Route("api/v1/purchase-returns"), ScopedRecord("purchase-return")]
[RequireModule(PlatformModules.Accounting)]
public class PurchaseReturnsController : TradeReturnsControllerBase
{
    public PurchaseReturnsController(IPurchaseReturnService service, IPermissionService permissions) : base(service, permissions, "purchases") { }
}
