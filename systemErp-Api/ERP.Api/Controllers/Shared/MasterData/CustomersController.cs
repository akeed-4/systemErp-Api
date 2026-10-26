using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

[Route("api/v1/customers"), RequireScreen("master-data")]
public class CustomersController : CrudController<CustomerDto, CreateCustomerDto, UpdateCustomerDto>
{
    private readonly ICustomerService _customers;
    public CustomersController(ICustomerService s) : base(s) => _customers = s;

    /// <summary>استيراد Excel - الفحص: كل صف بحالته (سليم/تنبيه/خطأ) وأسبابه، دون حفظ شيء.</summary>
    [HttpPost("import/preview"), RequireScreen("master-data", ScreenAction.Import, Legacy = ScreenAction.Create)]
    public async Task<IActionResult> PreviewImport([FromBody] List<PartyImportRowInput<CreateCustomerDto>> rows, CancellationToken ct)
        => Success(await _customers.PreviewImportAsync(rows, ct));

    /// <summary>استيراد Excel - الحفظ: يعيد الفحص ويحفظ السليمة وما عليه تنبيه، ويرفض صفوف الأخطاء بأسبابها.</summary>
    [HttpPost("import"), RequireScreen("master-data", ScreenAction.Import, Legacy = ScreenAction.Create)]
    public async Task<IActionResult> Import([FromBody] List<PartyImportRowInput<CreateCustomerDto>> rows, CancellationToken ct)
        => Success(await _customers.ImportRowsAsync(rows, ct));
}
