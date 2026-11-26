using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

/// <summary>فواتير الصرف المخزني: إخراج بتكلفة المخزون الفعلية، بلا ضريبة ولا عميل ولا هيئة.</summary>
[Route("api/v1/inventory-issues"), RequireScreen("inventory-issues"), RequireModule(PlatformModules.Accounting, PlatformModules.Pos)]
public class InventoryIssuesController : ErpControllerBase
{
    private const string Screen = "inventory-issues";
    private const string Kind = InventoryVoucherKinds.Issue;
    private readonly IInventoryVoucherService _vouchers;
    public InventoryIssuesController(IInventoryVoucherService vouchers) => _vouchers = vouchers;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PaginationParams query, CancellationToken ct) => Success(await _vouchers.ListAsync(Kind, query, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _vouchers.GetAsync(Kind, id, ct));

    /// <summary>الحساب المقابل الافتراضي وطريقة الجرد (لنموذج المستند الجديد).</summary>
    [HttpGet("defaults")]
    public async Task<IActionResult> Defaults(CancellationToken ct) => Success(await _vouchers.DefaultsAsync(Kind, ct));

    [HttpPost, RequireScreen(Screen, ScreenAction.Create)]
    public async Task<IActionResult> Create([FromBody] SaveInventoryVoucherDto request, CancellationToken ct)
    {
        if (request.Post) await InventoryVoucherPermissions.EnsurePostAsync(HttpContext, Screen, ct);
        return Success(await _vouchers.CreateAsync(Kind, request, ct));
    }

    [HttpPut("{id:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveInventoryVoucherDto request, CancellationToken ct)
    {
        if (request.Post) await InventoryVoucherPermissions.EnsurePostAsync(HttpContext, Screen, ct);
        return Success(await _vouchers.UpdateAsync(Kind, id, request, ct));
    }

    [HttpDelete("{id:guid}"), RequireScreen(Screen, ScreenAction.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _vouchers.DeleteAsync(Kind, id, ct);
        return Success("تم حذف المسودة.");
    }

    [HttpPost("{id:guid}/post"), RequireScreen(Screen, ScreenAction.Post)]
    public async Task<IActionResult> Post(Guid id, CancellationToken ct) => Success(await _vouchers.PostAsync(Kind, id, ct), "تم ترحيل فاتورة الصرف.");

    [HttpPost("{id:guid}/cancel"), RequireScreen(Screen, ScreenAction.Cancel)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelInventoryVoucherDto request, CancellationToken ct)
        => Success(await _vouchers.CancelAsync(Kind, id, request.Reason, ct), "تم إلغاء فاتورة الصرف وعكس أثرها.");
}

/// <summary>فواتير الاستلام المخزني: إدخال بسعر الشراء الصافي وقواعد الشراء في التكلفة، بلا ضريبة ولا مورد ولا هيئة.</summary>
[Route("api/v1/inventory-receipts"), RequireScreen("inventory-receipts"), RequireModule(PlatformModules.Accounting, PlatformModules.Pos)]
public class InventoryReceiptsController : ErpControllerBase
{
    private const string Screen = "inventory-receipts";
    private const string Kind = InventoryVoucherKinds.Receipt;
    private readonly IInventoryVoucherService _vouchers;
    public InventoryReceiptsController(IInventoryVoucherService vouchers) => _vouchers = vouchers;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PaginationParams query, CancellationToken ct) => Success(await _vouchers.ListAsync(Kind, query, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _vouchers.GetAsync(Kind, id, ct));

    [HttpGet("defaults")]
    public async Task<IActionResult> Defaults(CancellationToken ct) => Success(await _vouchers.DefaultsAsync(Kind, ct));

    [HttpPost, RequireScreen(Screen, ScreenAction.Create)]
    public async Task<IActionResult> Create([FromBody] SaveInventoryVoucherDto request, CancellationToken ct)
    {
        if (request.Post) await InventoryVoucherPermissions.EnsurePostAsync(HttpContext, Screen, ct);
        return Success(await _vouchers.CreateAsync(Kind, request, ct));
    }

    [HttpPut("{id:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveInventoryVoucherDto request, CancellationToken ct)
    {
        if (request.Post) await InventoryVoucherPermissions.EnsurePostAsync(HttpContext, Screen, ct);
        return Success(await _vouchers.UpdateAsync(Kind, id, request, ct));
    }

    [HttpDelete("{id:guid}"), RequireScreen(Screen, ScreenAction.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _vouchers.DeleteAsync(Kind, id, ct);
        return Success("تم حذف المسودة.");
    }

    [HttpPost("{id:guid}/post"), RequireScreen(Screen, ScreenAction.Post)]
    public async Task<IActionResult> Post(Guid id, CancellationToken ct) => Success(await _vouchers.PostAsync(Kind, id, ct), "تم ترحيل فاتورة الاستلام.");

    [HttpPost("{id:guid}/cancel"), RequireScreen(Screen, ScreenAction.Cancel)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelInventoryVoucherDto request, CancellationToken ct)
        => Success(await _vouchers.CancelAsync(Kind, id, request.Reason, ct), "تم إلغاء فاتورة الاستلام وعكس أثرها.");
}

/// <summary>«حفظ وترحيل» في طلب واحد يتطلب صلاحية الترحيل فوق الإنشاء/التعديل.</summary>
internal static class InventoryVoucherPermissions
{
    public static async Task EnsurePostAsync(HttpContext http, string screen, CancellationToken ct)
    {
        var permissions = http.RequestServices.GetRequiredService<IPermissionService>();
        if (!await permissions.HasPermissionAsync(screen, ScreenAction.Post, ct)) throw new ForbiddenException(Messages.InventoryVoucherPostForbidden);
    }
}
