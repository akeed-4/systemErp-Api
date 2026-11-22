using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>إعداد التأجير للعملاء: الفئات والأسعار وإعداد تأجير المعدات (شاشة إعدادات التأجير؛ العرض للقراءة والتعديل لكل كتابة).</summary>
[Route("api/v1/rental"), RequireScreen(Screen), RequireModule(PlatformModules.EquipmentRental)]
public class RentalSetupController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.RentalSettingsScreenId;
    private readonly IRentalSetupService _setup;
    public RentalSetupController(IRentalSetupService setup) => _setup = setup;

    [HttpGet("categories")]
    public async Task<IActionResult> Categories(CancellationToken ct) => Success(await _setup.CategoriesAsync(ct));

    [HttpPost("categories"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> CreateCategory([FromBody] SaveRentalCategoryDto request, CancellationToken ct)
        => Success(await _setup.CreateCategoryAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpPut("categories/{id:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> UpdateCategory(Guid id, [FromBody] SaveRentalCategoryDto request, CancellationToken ct)
        => Success(await _setup.UpdateCategoryAsync(id, request, ct));

    [HttpDelete("categories/{id:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> DeleteCategory(Guid id, CancellationToken ct)
    {
        await _setup.DeleteCategoryAsync(id, ct);
        return Success(Messages.DeletedSuccessfully);
    }

    [HttpGet("rates")]
    public async Task<IActionResult> Rates([FromQuery] Guid? categoryId, [FromQuery] Guid? equipmentId, CancellationToken ct)
        => Success(await _setup.RatesAsync(categoryId, equipmentId, ct));

    [HttpPost("rates"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> CreateRate([FromBody] SaveRentalRateDto request, CancellationToken ct)
        => Success(await _setup.CreateRateAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpPut("rates/{id:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> UpdateRate(Guid id, [FromBody] SaveRentalRateDto request, CancellationToken ct)
        => Success(await _setup.UpdateRateAsync(id, request, ct));

    [HttpDelete("rates/{id:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> DeleteRate(Guid id, CancellationToken ct)
    {
        await _setup.DeleteRateAsync(id, ct);
        return Success(Messages.DeletedSuccessfully);
    }

    /// <summary>سجل المعدات بإعداد تأجيرها؛ `rentableOnly` للقابلة للتأجير. القراءة متاحة لمن يحجز أيضاً.</summary>
    [HttpGet("equipment"), RequireScreen(Screen, AlsoAllow = PermissionCatalog.RentalBookingsScreenId)]
    public async Task<IActionResult> Equipment([FromQuery] bool rentableOnly, CancellationToken ct) => Success(await _setup.EquipmentAsync(rentableOnly, ct));

    [HttpPut("equipment/{id:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> UpdateEquipment(Guid id, [FromBody] UpdateRentalEquipmentDto request, CancellationToken ct)
        => Success(await _setup.UpdateEquipmentAsync(id, request, ct));
}

/// <summary>
/// عقود التأجير (شاشة عقود التأجير): «اعتماد» = تفعيل العقد، و«تعديل» = تعديل المسودة وتمديد الساري، و«إلغاء» = إلغاؤه بسبب،
/// و«خصم يدوي» = سعر يدوي لسطر (تفحصه الخدمة). التحويل من حجز إنشاء عقد.
/// </summary>
[Route("api/v1/rental"), RequireScreen(Screen), RequireModule(PlatformModules.EquipmentRental)]
public class RentalContractsController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.RentalContractsScreenId;
    private readonly IRentalContractService _contracts;
    public RentalContractsController(IRentalContractService contracts) => _contracts = contracts;

    [HttpGet("contracts")]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] Guid? customerId, CancellationToken ct)
        => Success(await _contracts.ListAsync(status, customerId, ct));

    [HttpGet("contracts/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _contracts.GetAsync(id, ct));

    [HttpPost("contracts")]
    public async Task<IActionResult> Create([FromBody] SaveRentalContractDto request, CancellationToken ct)
        => Success(await _contracts.CreateAsync(request, ct), Messages.CreatedSuccessfully);

    /// <summary>تحويل حجز مؤكد أو مؤقت ساري إلى مسودة عقد (`term` = short|long).</summary>
    [HttpPost("bookings/{id:guid}/convert"), RequireScreen(Screen, ScreenAction.Create)]
    public async Task<IActionResult> Convert(Guid id, [FromQuery] string term, CancellationToken ct)
        => Success(await _contracts.ConvertFromBookingAsync(id, term, ct), Messages.CreatedSuccessfully);

    [HttpPut("contracts/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveRentalContractDto request, CancellationToken ct)
        => Success(await _contracts.UpdateAsync(id, request, ct));

    [HttpPost("contracts/{id:guid}/activate"), RequireScreen(Screen, ScreenAction.Approve)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct) => Success(await _contracts.ActivateAsync(id, ct));

    [HttpPost("contracts/{id:guid}/extend"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> Extend(Guid id, [FromBody] ExtendRentalContractDto request, CancellationToken ct)
        => Success(await _contracts.ExtendAsync(id, request, ct));

    /// <summary>تجديد الساري بعقد لاحق (مسودة) من نهاية أسطره؛ إنشاء عقد.</summary>
    [HttpPost("contracts/{id:guid}/renew"), RequireScreen(Screen, ScreenAction.Create)]
    public async Task<IActionResult> Renew(Guid id, [FromBody] RenewRentalContractDto request, CancellationToken ct)
        => Success(await _contracts.RenewAsync(id, request, ct), Messages.CreatedSuccessfully);

    /// <summary>تعيين مشغّل سطر بمشغّل أو تغييره (تعديل العقد).</summary>
    [HttpPost("contracts/{id:guid}/lines/{lineId:guid}/operator"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> AssignOperator(Guid id, Guid lineId, [FromBody] AssignRentalOperatorDto request, CancellationToken ct)
        => Success(await _contracts.AssignOperatorAsync(id, lineId, request, ct));

    [HttpPost("contracts/{id:guid}/cancel"), RequireScreen(Screen, ScreenAction.Cancel)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] RentalReasonDto request, CancellationToken ct)
        => Success(await _contracts.CancelAsync(id, request, ct));

    [HttpDelete("contracts/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _contracts.DeleteAsync(id, ct);
        return Success(Messages.DeletedSuccessfully);
    }
}

/// <summary>
/// محاضر التسليم والاستلام (شاشة المحاضر): الإنشاء يرحّل المحضر فوراً، و«إلغاء» = عكسه بسبب. الصور والتوقيع عبر المرفقات المشتركة
/// (`api/v1/attachments/rental_handover/{id}`).
/// </summary>
[Route("api/v1/rental/handovers"), RequireScreen(Screen), RequireModule(PlatformModules.EquipmentRental)]
public class RentalHandoversController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.RentalHandoversScreenId;
    private readonly IRentalHandoverService _handovers;
    public RentalHandoversController(IRentalHandoverService handovers) => _handovers = handovers;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? contractId, [FromQuery] string? kind, CancellationToken ct)
        => Success(await _handovers.ListAsync(contractId, kind, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _handovers.GetAsync(id, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveRentalHandoverDto request, CancellationToken ct)
        => Success(await _handovers.CreateAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpPost("{id:guid}/cancel"), RequireScreen(Screen, ScreenAction.Cancel)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] RentalReasonDto request, CancellationToken ct)
        => Success(await _handovers.CancelAsync(id, request, ct));
}

/// <summary>التوفر والتقدير والحجوزات (شاشة حجوزات التأجير): «اعتماد» = تأكيد الحجز، و«إلغاء» = إلغاؤه بسبب.</summary>
[Route("api/v1/rental"), RequireScreen(Screen), RequireModule(PlatformModules.EquipmentRental)]
public class RentalBookingsController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.RentalBookingsScreenId;
    private readonly IRentalBookingService _bookings;
    public RentalBookingsController(IRentalBookingService bookings) => _bookings = bookings;

    /// <summary>الفترات المشغولة لكل معدة قابلة للتأجير بين تاريخين (بحد 400 يوم)، اختيارياً لفئة (وفروعها) أو موقع.</summary>
    [HttpGet("availability")]
    public async Task<IActionResult> Availability([FromQuery] DateTime from, [FromQuery] DateTime to, [FromQuery] Guid? categoryId, [FromQuery] Guid? warehouseId,
        CancellationToken ct)
        => Success(await _bookings.CalendarAsync(from, to, categoryId, warehouseId, ct));

    /// <summary>تقدير الإيجار للفترة وتعارضات المعدة (قراءة بلا حفظ).</summary>
    [HttpPost("quote"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Quote([FromBody] RentalQuoteRequestDto request, CancellationToken ct) => Success(await _bookings.QuoteAsync(request, ct));

    [HttpGet("bookings")]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] Guid? customerId, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        CancellationToken ct)
        => Success(await _bookings.ListAsync(status, customerId, from, to, ct));

    [HttpGet("bookings/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _bookings.GetAsync(id, ct));

    [HttpPost("bookings")]
    public async Task<IActionResult> Create([FromBody] SaveRentalBookingDto request, CancellationToken ct)
        => Success(await _bookings.CreateAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpPut("bookings/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveRentalBookingDto request, CancellationToken ct)
        => Success(await _bookings.UpdateAsync(id, request, ct));

    [HttpPost("bookings/{id:guid}/hold"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> Hold(Guid id, [FromBody] HoldRentalBookingDto request, CancellationToken ct)
        => Success(await _bookings.HoldAsync(id, request, ct));

    [HttpPost("bookings/{id:guid}/confirm"), RequireScreen(Screen, ScreenAction.Approve)]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken ct) => Success(await _bookings.ConfirmAsync(id, ct));

    [HttpPost("bookings/{id:guid}/cancel"), RequireScreen(Screen, ScreenAction.Cancel)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] RentalReasonDto request, CancellationToken ct)
        => Success(await _bookings.CancelAsync(id, request, ct));

    [HttpDelete("bookings/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _bookings.DeleteAsync(id, ct);
        return Success(Messages.DeletedSuccessfully);
    }
}

/// <summary>
/// مهام نقل معدات العقود (ضمن شاشة المحاضر): «اعتماد» = تنفيذ المهمة، و«إلغاء» = إلغاؤها بسبب. لا قيود محاسبية.
/// </summary>
[Route("api/v1/rental/transport"), RequireScreen(Screen), RequireModule(PlatformModules.EquipmentRental)]
public class RentalTransportController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.RentalHandoversScreenId;
    private readonly IRentalTransportService _transport;
    public RentalTransportController(IRentalTransportService transport) => _transport = transport;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? contractId, [FromQuery] string? status, CancellationToken ct)
        => Success(await _transport.ListAsync(contractId, status, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _transport.GetAsync(id, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveRentalTransportJobDto request, CancellationToken ct)
        => Success(await _transport.CreateAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveRentalTransportJobDto request, CancellationToken ct)
        => Success(await _transport.UpdateAsync(id, request, ct));

    [HttpPost("{id:guid}/complete"), RequireScreen(Screen, ScreenAction.Post)]
    public async Task<IActionResult> Complete(Guid id, [FromBody] CompleteRentalTransportJobDto request, CancellationToken ct)
        => Success(await _transport.CompleteAsync(id, request, ct));

    [HttpPost("{id:guid}/cancel"), RequireScreen(Screen, ScreenAction.Cancel)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] RentalReasonDto request, CancellationToken ct)
        => Success(await _transport.CancelAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _transport.DeleteAsync(id, ct);
        return Success(Messages.DeletedSuccessfully);
    }
}

/// <summary>
/// الرسوم والفوترة (شاشة الرسوم والفوترة): «إنشاء» = رسم يدوي أو توليد الإضافي والتأخير والنقل، «اعتماد» = اعتماد الرسم بتصنيفه،
/// «ترحيل» = إصدار فاتورة الفترة، «إلغاء» = إلغاء آخر فوترة بسبب. الإعفاء من رسم بالشاشة المقيدة `rental-charge-waiver` (تفرضه الخدمة).
/// الإعداد وأنواع الرسوم بشاشة إعدادات التأجير، وإغلاق العقد بشاشة العقود (إغلاق).
/// </summary>
[Route("api/v1/rental"), RequireScreen(Screen), RequireModule(PlatformModules.EquipmentRental)]
public class RentalBillingController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.RentalBillingScreenId;
    private const string Settings = PermissionCatalog.RentalSettingsScreenId;
    private readonly IRentalBillingService _billing;
    public RentalBillingController(IRentalBillingService billing) => _billing = billing;

    [HttpGet("billing-settings"), RequireScreen(Settings, AlsoAllow = Screen)]
    public async Task<IActionResult> GetSettings(CancellationToken ct) => Success(await _billing.SettingsAsync(ct));

    [HttpPut("billing-settings"), RequireScreen(Settings, ScreenAction.Edit)]
    public async Task<IActionResult> SaveSettings([FromBody] RentalSettingsDto request, CancellationToken ct) => Success(await _billing.SaveSettingsAsync(request, ct));

    [HttpGet("charge-types"), RequireScreen(Settings, AlsoAllow = Screen)]
    public async Task<IActionResult> ChargeTypes(CancellationToken ct) => Success(await _billing.ChargeTypesAsync(ct));

    [HttpPost("charge-types"), RequireScreen(Settings, ScreenAction.Edit)]
    public async Task<IActionResult> CreateChargeType([FromBody] SaveRentalChargeTypeDto request, CancellationToken ct)
        => Success(await _billing.SaveChargeTypeAsync(request, null, ct), Messages.CreatedSuccessfully);

    [HttpPut("charge-types/{id:guid}"), RequireScreen(Settings, ScreenAction.Edit)]
    public async Task<IActionResult> UpdateChargeType(Guid id, [FromBody] SaveRentalChargeTypeDto request, CancellationToken ct)
        => Success(await _billing.SaveChargeTypeAsync(request, id, ct));

    [HttpGet("charges")]
    public async Task<IActionResult> Charges([FromQuery] Guid? contractId, [FromQuery] string? status, CancellationToken ct)
        => Success(await _billing.ChargesAsync(contractId, status, ct));

    [HttpPost("charges")]
    public async Task<IActionResult> AddCharge([FromBody] SaveRentalChargeDto request, CancellationToken ct)
        => Success(await _billing.AddChargeAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpPost("charges/{id:guid}/approve"), RequireScreen(Screen, ScreenAction.Approve)]
    public async Task<IActionResult> ApproveCharge(Guid id, [FromBody] ApproveRentalChargeDto request, CancellationToken ct)
        => Success(await _billing.ApproveChargeAsync(id, request, ct));

    [HttpPost("charges/{id:guid}/waive"), RequireScreen(Screen, ScreenAction.Approve)]
    public async Task<IActionResult> WaiveCharge(Guid id, [FromBody] RentalReasonDto request, CancellationToken ct)
        => Success(await _billing.WaiveChargeAsync(id, request, ct));

    [HttpDelete("charges/{id:guid}")]
    public async Task<IActionResult> DeleteCharge(Guid id, CancellationToken ct)
    {
        await _billing.DeleteChargeAsync(id, ct);
        return Success(Messages.DeletedSuccessfully);
    }

    [HttpPost("contracts/{id:guid}/generate-charges"), RequireScreen(Screen, ScreenAction.Create)]
    public async Task<IActionResult> Generate(Guid id, CancellationToken ct) => Success(await _billing.GenerateChargesAsync(id, ct));

    [HttpGet("contracts/{id:guid}/billing-preview")]
    public async Task<IActionResult> Preview(Guid id, [FromQuery] DateTime? until, CancellationToken ct) => Success(await _billing.PreviewAsync(id, until, ct));

    [HttpPost("contracts/{id:guid}/bill"), RequireScreen(Screen, ScreenAction.Post)]
    public async Task<IActionResult> Bill(Guid id, [FromBody] BillRentalContractDto request, CancellationToken ct)
        => Success(await _billing.BillAsync(id, request, ct), Messages.CreatedSuccessfully);

    [HttpGet("billings")]
    public async Task<IActionResult> Billings([FromQuery] Guid? contractId, CancellationToken ct) => Success(await _billing.BillingsAsync(contractId, ct));

    [HttpPost("billings/{id:guid}/cancel"), RequireScreen(Screen, ScreenAction.Cancel)]
    public async Task<IActionResult> CancelBilling(Guid id, [FromBody] RentalReasonDto request, CancellationToken ct)
        => Success(await _billing.CancelBillingAsync(id, request, ct));

    [HttpPost("contracts/{id:guid}/close"), RequireScreen(PermissionCatalog.RentalContractsScreenId, ScreenAction.Close)]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct) => Success(await _billing.CloseContractAsync(id, ct));
}

/// <summary>
/// كشوف ساعات المعدات (T1): الإنشاء والتعديل للمسودة والمرفوض، والتقديم، واعتماد ممثل العميل أو رفضه، وإعادة فتح المعتمد غير المفوتر.
/// صورة الكشف الموقّع عبر المرفقات المشتركة (`api/v1/attachments/rental_timesheet/{id}`).
/// </summary>
[Route("api/v1/rental/timesheets"), RequireScreen(Screen), RequireModule(PlatformModules.EquipmentRental)]
public class RentalTimesheetsController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.RentalTimesheetsScreenId;
    private readonly IRentalTimesheetService _timesheets;
    public RentalTimesheetsController(IRentalTimesheetService timesheets) => _timesheets = timesheets;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? contractId, [FromQuery] Guid? lineId, [FromQuery] string? status, CancellationToken ct)
        => Success(await _timesheets.ListAsync(contractId, lineId, status, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _timesheets.GetAsync(id, ct));

    [HttpGet("draft")]
    public async Task<IActionResult> Draft([FromQuery] Guid contractId, [FromQuery] Guid lineId, [FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct)
        => Success(await _timesheets.DraftAsync(contractId, lineId, from, to, ct));

    [HttpPost, RequireScreen(Screen, ScreenAction.Create)]
    public async Task<IActionResult> Create([FromBody] SaveRentalTimesheetDto request, CancellationToken ct)
        => Success(await _timesheets.CreateAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpPut("{id:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveRentalTimesheetDto request, CancellationToken ct)
        => Success(await _timesheets.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}"), RequireScreen(Screen, ScreenAction.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _timesheets.DeleteAsync(id, ct);
        return Success(Messages.DeletedSuccessfully);
    }

    [HttpPost("{id:guid}/submit"), RequireScreen(Screen, ScreenAction.Submit)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct) => Success(await _timesheets.SubmitAsync(id, ct));

    [HttpPost("{id:guid}/approve"), RequireScreen(Screen, ScreenAction.Approve)]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApproveRentalTimesheetDto request, CancellationToken ct)
        => Success(await _timesheets.ApproveAsync(id, request, ct));

    [HttpPost("{id:guid}/reject"), RequireScreen(Screen, ScreenAction.Approve)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RentalReasonDto request, CancellationToken ct)
        => Success(await _timesheets.RejectAsync(id, request, ct));

    [HttpPost("{id:guid}/reopen"), RequireScreen(Screen, ScreenAction.Reopen)]
    public async Task<IActionResult> Reopen(Guid id, [FromBody] RentalReasonDto request, CancellationToken ct)
        => Success(await _timesheets.ReopenAsync(id, request, ct));
}
