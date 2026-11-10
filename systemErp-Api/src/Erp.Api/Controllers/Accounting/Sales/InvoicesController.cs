using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>فواتير البيع والشراء ومرتجعاتهما. الصلاحية تُحدَّد حسب نوع المستند (sales / sales-returns / purchases).</summary>
[Route("api/v1/invoices")]
public class InvoicesController : ErpControllerBase
{
    private readonly IInvoiceService _invoices;
    private readonly IPermissionService _permissions;

    public InvoicesController(IInvoiceService invoices, IPermissionService permissions)
    {
        _invoices = invoices; _permissions = permissions;
    }

    private static string ScreenFor(InvoiceKind k) => k switch
    {
        InvoiceKind.Sales => "sales", InvoiceKind.SalesReturn => "sales-returns", _ => "purchases",
    };

    private async Task Require(InvoiceKind kind, ScreenAction action, CancellationToken ct)
    {
        if (!await _permissions.HasPermissionAsync(ScreenFor(kind), action, ct)) throw new ForbiddenException();
    }

    /// <summary>بلا kind يلزم عرض المبيعات والمشتريات معًا (كما كان)؛ مع kind تكفي صلاحية شاشة ذلك النوع.</summary>
    private async Task RequireListView(InvoiceKind? kind, CancellationToken ct)
    {
        if (kind.HasValue) await Require(kind.Value, ScreenAction.View, ct);
        else { await Require(InvoiceKind.Sales, ScreenAction.View, ct); await Require(InvoiceKind.Purchase, ScreenAction.View, ct); }
    }

    /// <summary>
    /// صفحة من الفواتير. مرشّحات اختيارية: kind، hasVehicleLines، paymentType (cash|credit)، originalHasVehicleLines، invoiceNumber (مطابقة تامة)،
    /// إضافة إلى PaginationParams: pageNumber/pageSize (≤ 1000)، searchTerm، sortBy/isDescending، startDate/endDate (اليوم الأخير شامل)، status.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] InvoiceListQueryDto filter, [FromQuery] PaginationParams q, CancellationToken ct)
    {
        await RequireListView(filter.Kind, ct);
        return Success(await _invoices.ListAsync(filter, q, ct));
    }

    /// <summary>إجماليات كامل النتيجة المرشّحة (نفس مرشّحات القائمة وصلاحياتها)، لا الصفحة الحالية.</summary>
    [HttpGet("Summary")]
    public async Task<IActionResult> Summary([FromQuery] InvoiceListQueryDto filter, [FromQuery] PaginationParams q, CancellationToken ct)
    {
        await RequireListView(filter.Kind, ct);
        return Success(await _invoices.SummaryAsync(filter, q, ct));
    }

    /// <summary>
    /// البحث العام (شريط البحث): فواتير البيع ومرتجعاتها وفواتير الشراء، مقيّدًا بالأنواع التي يملك المستخدم صلاحية عرض شاشتها فقط.
    /// لا صلاحية على أي نوع = 403. take ≤ 50.
    /// </summary>
    [HttpGet("Search")]
    public async Task<IActionResult> Search([FromQuery] string? term, [FromQuery] int take = 10, CancellationToken ct = default)
    {
        var allowed = new List<InvoiceKind>();
        foreach (var kind in new[] { InvoiceKind.Sales, InvoiceKind.SalesReturn, InvoiceKind.Purchase })
            if (await _permissions.HasPermissionAsync(ScreenFor(kind), ScreenAction.View, ct)) allowed.Add(kind);
        if (allowed.Count == 0) throw new ForbiddenException();
        return Success(await _invoices.SearchAsync(term ?? string.Empty, allowed, take, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var invoice = await _invoices.GetAsync(id, ct);
        await Require(invoice.Kind, ScreenAction.View, ct);
        return Success(invoice);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateInvoiceDto dto, CancellationToken ct)
    {
        await Require(dto.Kind, ScreenAction.Create, ct);
        var created = await _invoices.CreateAsync(dto, ct);
        return Created($"{Request.Path}/{created.Id}", ApiResponse<InvoiceDto>.Ok(created, "تم إنشاء الفاتورة").WithStatus(201));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateInvoiceDto dto, CancellationToken ct)
    {
        await Require(dto.Kind, ScreenAction.Edit, ct);
        return Success(await _invoices.UpdateAsync(id, dto, ct), "تم تعديل الفاتورة");
    }

    [HttpPost("returns")]
    public async Task<IActionResult> CreateReturn([FromBody] CreateReturnInvoiceRequestDto dto, CancellationToken ct)
    {
        var original = await _invoices.GetAsync(dto.OriginalInvoiceId, ct);
        await Require(original.Kind == InvoiceKind.Sales ? InvoiceKind.SalesReturn : InvoiceKind.PurchaseReturn, ScreenAction.Create, ct);
        return Success(await _invoices.CreateReturnAsync(dto, ct), "تم إنشاء المرتجع");
    }

    [HttpPost("{id:guid}/post")]
    public async Task<IActionResult> Post(Guid id, CancellationToken ct)
    {
        await Require((await _invoices.GetAsync(id, ct)).Kind, ScreenAction.Edit, ct);
        return Success(await _invoices.PostDraftAsync(id, ct), "تم ترحيل الفاتورة");
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await Require((await _invoices.GetAsync(id, ct)).Kind, ScreenAction.Delete, ct);
        await _invoices.DeleteAsync(id, ct);
        return Success("تم حذف الفاتورة");
    }

    /// <summary>معاينة القيد المحاسبي (مخزون، تكلفة إيرادات، ضريبة، دفع/ذمم) قبل الحفظ أو الترحيل — بلا أي أثر.</summary>
    [HttpPost("PreviewJournal")]
    public async Task<IActionResult> PreviewJournal([FromBody] CreateInvoiceDto dto, CancellationToken ct)
    {
        await Require(dto.Kind, ScreenAction.View, ct);
        return Success(await _invoices.PreviewJournalAsync(dto, ct));
    }

    [HttpGet("{id:guid}/journal")]
    public async Task<IActionResult> Journal(Guid id, CancellationToken ct)
    {
        await Require((await _invoices.GetAsync(id, ct)).Kind, ScreenAction.View, ct);
        return Success(await _invoices.GetJournalAsync(id, ct));
    }

    [HttpPost("{id:guid}/SubmitZatca")]
    public async Task<IActionResult> SubmitZatca(Guid id, CancellationToken ct)
    {
        if (!await _permissions.HasPermissionAsync("zatca", ScreenAction.Edit, ct)) throw new ForbiddenException();
        return Success(await _invoices.SubmitToZatcaAsync(id, ct));
    }
}
