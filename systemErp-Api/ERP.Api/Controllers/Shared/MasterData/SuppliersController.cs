using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

[Route("api/v1/suppliers"), RequireScreen("master-data")]
public class SuppliersController : CrudController<SupplierDto, CreateSupplierDto, UpdateSupplierDto>
{
    private readonly ISupplierService _suppliers;
    public SuppliersController(ISupplierService s) : base(s) => _suppliers = s;

    /// <summary>استيراد Excel - الفحص: كل صف بحالته (سليم/تنبيه/خطأ) وأسبابه، دون حفظ شيء.</summary>
    [HttpPost("import/preview"), RequireScreen("master-data", ScreenAction.Import, Legacy = ScreenAction.Create)]
    public async Task<IActionResult> PreviewImport([FromBody] List<PartyImportRowInput<CreateSupplierDto>> rows, CancellationToken ct)
        => Success(await _suppliers.PreviewImportAsync(rows, ct));

    /// <summary>استيراد Excel - الحفظ: يعيد الفحص ويحفظ السليمة وما عليه تنبيه، ويرفض صفوف الأخطاء بأسبابها.</summary>
    [HttpPost("import"), RequireScreen("master-data", ScreenAction.Import, Legacy = ScreenAction.Create)]
    public async Task<IActionResult> Import([FromBody] List<PartyImportRowInput<CreateSupplierDto>> rows, CancellationToken ct)
        => Success(await _suppliers.ImportRowsAsync(rows, ct));
}
