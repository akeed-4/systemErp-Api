using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>
/// المعدات والآليات (وحدة المقاولات): السجل، والإسناد للمشاريع والتحويل والإرجاع، وسجل التشغيل، وعقود الإيجار، واللوحة وتقرير التكلفة.
/// الإسناد ينشئ مورد المشروع بقواعد خدمة المشاريع (العقد غير مغلق)، فتُحمَّل تكلفة الساعة من التقرير اليومي المعتمد.
/// السجل وسجل التشغيل وعقود الاستئجار من المورد متاحة لإضافة التأجير أيضاً (سجل واحد للمعدات)؛ الإسناد للمشاريع وتسعيرات إيرادها
/// وربحيتها وتوزيع الإهلاك وتقرير التكلفة للمقاولات وحدها (سمة على الإجراء).
/// </summary>
[Route("api/v1/equipment"), RequireScreen(PermissionCatalog.EquipmentScreenId)]
[RequireModule(PlatformModules.Construction, PlatformModules.EquipmentRental)]
[RequireFeature(PlanFeatures.CommercialCycle)]
public class EquipmentController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.EquipmentScreenId;
    private readonly IEquipmentService _equipment;

    public EquipmentController(IEquipmentService equipment) => _equipment = equipment;

    // ---------- السجل ----------

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Success(await _equipment.ListAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _equipment.GetAsync(id, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveEquipmentDto request, CancellationToken ct)
        => Success(await _equipment.CreateAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveEquipmentDto request, CancellationToken ct)
        => Success(await _equipment.UpdateAsync(id, request, ct), Messages.UpdatedSuccessfully);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _equipment.DeleteAsync(id, ct);
        return Success(Messages.DeletedSuccessfully);
    }

    /// <summary>خيارات النماذج: المشاريع والموظفون (المشغّلون) والأصول الثابتة والتصنيفات.</summary>
    [HttpGet("options"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Options(CancellationToken ct) => Success(await _equipment.OptionsAsync(ct));

    [HttpGet("dashboard"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Dashboard(CancellationToken ct) => Success(await _equipment.DashboardAsync(ct));

    [HttpGet("cost-report"), RequireScreen(Screen, ScreenAction.View), RequireModule(PlatformModules.Construction)]
    public async Task<IActionResult> CostReport([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => Success(await _equipment.CostReportAsync(from, to, ct));

    // ---------- الإيرادات والربحية (حساب بلا قيود) ----------

    /// <summary>ربحية المعدات لفترة: الإيراد التشغيلي المحسوب والمحاسبي المعتمد في المستخلصات مقابل التكلفة الفعلية، لكل معدة ومشروع.</summary>
    [HttpGet("profitability"), RequireScreen(Screen, ScreenAction.View), RequireModule(PlatformModules.Construction)]
    public async Task<IActionResult> Profitability([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] Guid? contractId,
        [FromServices] IEquipmentProfitabilityService profit, CancellationToken ct)
        => Success(await profit.ReportAsync(from, to, contractId, ct));

    [HttpGet("revenue-rates"), RequireScreen(Screen, ScreenAction.View), RequireModule(PlatformModules.Construction)]
    public async Task<IActionResult> RevenueRates([FromQuery] Guid? equipmentId, [FromQuery] Guid? contractId, [FromServices] IEquipmentProfitabilityService profit, CancellationToken ct)
        => Success(await profit.RatesAsync(equipmentId, contractId, ct));

    /// <summary>بنود جدول كميات العقد القابلة للربط بتسعيرة معدة.</summary>
    [HttpGet("revenue-rates/items"), RequireScreen(Screen, ScreenAction.View), RequireModule(PlatformModules.Construction)]
    public async Task<IActionResult> RevenueItems([FromQuery] Guid contractId, [FromServices] IEquipmentProfitabilityService profit, CancellationToken ct)
        => Success(await profit.ItemOptionsAsync(contractId, ct));

    [HttpPost("revenue-rates"), RequireScreen(Screen, ScreenAction.Edit), RequireModule(PlatformModules.Construction)]
    public async Task<IActionResult> CreateRevenueRate([FromBody] SaveEquipmentRevenueRateDto request, [FromServices] IEquipmentProfitabilityService profit, CancellationToken ct)
        => Success(await profit.CreateRateAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpPut("revenue-rates/{rateId:guid}"), RequireScreen(Screen, ScreenAction.Edit), RequireModule(PlatformModules.Construction)]
    public async Task<IActionResult> UpdateRevenueRate(Guid rateId, [FromBody] SaveEquipmentRevenueRateDto request, [FromServices] IEquipmentProfitabilityService profit, CancellationToken ct)
        => Success(await profit.UpdateRateAsync(rateId, request, ct), Messages.UpdatedSuccessfully);

    [HttpDelete("revenue-rates/{rateId:guid}"), RequireScreen(Screen, ScreenAction.Edit), RequireModule(PlatformModules.Construction)]
    public async Task<IActionResult> DeleteRevenueRate(Guid rateId, [FromServices] IEquipmentProfitabilityService profit, CancellationToken ct)
    {
        await profit.DeleteRateAsync(rateId, ct);
        return Success(Messages.DeletedSuccessfully);
    }

    /// <summary>
    /// إعادة توزيع الإهلاك الفعلي لمعدات «طريقة الإهلاك» على المشاريع لفترة (yyyy-MM) بالفرق عمّا رُحِّل (بعد تصحيحات متأخرة)؛ يتم آلياً
    /// عند ترحيل الإهلاك وعكسه واعتماد التقارير. يرحّل قيوداً فيحتاج صلاحية الترحيل.
    /// </summary>
    [HttpPost("depreciation-allocation"), RequireScreen(Screen, ScreenAction.Post), RequireModule(PlatformModules.Construction)]
    public async Task<IActionResult> ReallocateDepreciation([FromQuery] string period, [FromServices] IEquipmentCostAllocationService allocation, CancellationToken ct)
        => Success(new { assets = await allocation.ReallocatePeriodAsync(period, ct) });

    // ---------- الإسناد (الإسناد والتحويل والإرجاع = تعديل حالة المعدة) ----------

    [HttpGet("assignments"), RequireScreen(Screen, ScreenAction.View), RequireModule(PlatformModules.Construction)]
    public async Task<IActionResult> Assignments([FromQuery] Guid? equipmentId, [FromQuery] Guid? contractId, [FromQuery] bool activeOnly, CancellationToken ct)
        => Success(await _equipment.AssignmentsAsync(equipmentId, contractId, activeOnly, ct));

    [HttpPost("assignments"), RequireScreen(Screen, ScreenAction.Edit), RequireModule(PlatformModules.Construction)]
    public async Task<IActionResult> Assign([FromBody] AssignEquipmentDto request, CancellationToken ct)
        => Success(await _equipment.AssignAsync(request, ct), "تم إسناد المعدة للمشروع.");

    [HttpPut("assignments/{assignmentId:guid}"), RequireScreen(Screen, ScreenAction.Edit), RequireModule(PlatformModules.Construction)]
    public async Task<IActionResult> UpdateAssignment(Guid assignmentId, [FromBody] UpdateEquipmentAssignmentDto request, CancellationToken ct)
        => Success(await _equipment.UpdateAssignmentAsync(assignmentId, request, ct));

    [HttpPost("assignments/{assignmentId:guid}/return"), RequireScreen(Screen, ScreenAction.Edit), RequireModule(PlatformModules.Construction)]
    public async Task<IActionResult> Return(Guid assignmentId, [FromBody] ReturnEquipmentDto request, CancellationToken ct)
        => Success(await _equipment.ReturnAsync(assignmentId, request, ct), "تم إرجاع المعدة من المشروع.");

    [HttpPost("assignments/{assignmentId:guid}/transfer"), RequireScreen(Screen, ScreenAction.Edit), RequireModule(PlatformModules.Construction)]
    public async Task<IActionResult> Transfer(Guid assignmentId, [FromBody] TransferEquipmentDto request, CancellationToken ct)
        => Success(await _equipment.TransferAsync(assignmentId, request, ct), "تم تحويل المعدة للمشروع الجديد.");

    // ---------- سجل التشغيل (التسجيل بقيد يتطلب صلاحية الترحيل) ----------

    [HttpGet("logs"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Logs([FromQuery] Guid? equipmentId, [FromQuery] string? kind, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => Success(await _equipment.LogsAsync(equipmentId, kind, from, to, ct));

    [HttpPost("logs"), RequireScreen(Screen, ScreenAction.Create)]
    public async Task<IActionResult> AddLog([FromBody] SaveEquipmentLogDto request, [FromServices] IPermissionService permissions, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.CreditAccountCode) && !await permissions.HasPermissionAsync(Screen, ScreenAction.Post, ct)) throw new ForbiddenException();
        return Success(await _equipment.AddLogAsync(request, ct));
    }

    [HttpDelete("logs/{logId:guid}"), RequireScreen(Screen, ScreenAction.Delete)]
    public async Task<IActionResult> DeleteLog(Guid logId, CancellationToken ct)
    {
        await _equipment.DeleteLogAsync(logId, ct);
        return Success(Messages.DeletedSuccessfully);
    }

    // ---------- عقود الإيجار (التفعيل = اعتماد، والإغلاق = إغلاق) ----------

    [HttpGet("rentals"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Rentals([FromQuery] Guid? equipmentId, CancellationToken ct) => Success(await _equipment.RentalsAsync(equipmentId, ct));

    [HttpPost("rentals"), RequireScreen(Screen, ScreenAction.Create)]
    public async Task<IActionResult> CreateRental([FromBody] SaveEquipmentRentalContractDto request, CancellationToken ct)
        => Success(await _equipment.CreateRentalAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpPut("rentals/{rentalId:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> UpdateRental(Guid rentalId, [FromBody] SaveEquipmentRentalContractDto request, CancellationToken ct)
        => Success(await _equipment.UpdateRentalAsync(rentalId, request, ct), Messages.UpdatedSuccessfully);

    [HttpPost("rentals/{rentalId:guid}/activate"), RequireScreen(Screen, ScreenAction.Approve)]
    public async Task<IActionResult> ActivateRental(Guid rentalId, CancellationToken ct)
        => Success(await _equipment.ActivateRentalAsync(rentalId, ct), "تم تفعيل عقد الإيجار.");

    [HttpPost("rentals/{rentalId:guid}/close"), RequireScreen(Screen, ScreenAction.Close)]
    public async Task<IActionResult> CloseRental(Guid rentalId, CancellationToken ct)
        => Success(await _equipment.CloseRentalAsync(rentalId, ct), "تم إغلاق عقد الإيجار.");

    [HttpDelete("rentals/{rentalId:guid}"), RequireScreen(Screen, ScreenAction.Delete)]
    public async Task<IActionResult> DeleteRental(Guid rentalId, CancellationToken ct)
    {
        await _equipment.DeleteRentalAsync(rentalId, ct);
        return Success(Messages.DeletedSuccessfully);
    }
}

/// <summary>أوامر عمل صيانة المعدات بقطع غيارها: الإكمال (ترحيل) يصرف القطع ويقيد التكلفة، والإلغاء يعكسها.</summary>
[Route("api/v1/equipment-maintenance"), RequireScreen(PermissionCatalog.EquipmentMaintenanceScreenId)]
[RequireModule(PlatformModules.Construction, PlatformModules.EquipmentRental)]
[RequireFeature(PlanFeatures.CommercialCycle)]
public class EquipmentMaintenanceController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.EquipmentMaintenanceScreenId;
    private readonly IEquipmentMaintenanceService _orders;

    public EquipmentMaintenanceController(IEquipmentMaintenanceService orders) => _orders = orders;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? equipmentId, [FromQuery] string? status, CancellationToken ct)
        => Success(await _orders.ListAsync(equipmentId, status, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _orders.GetAsync(id, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveEquipmentMaintenanceOrderDto request, CancellationToken ct)
        => Success(await _orders.CreateAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveEquipmentMaintenanceOrderDto request, CancellationToken ct)
        => Success(await _orders.UpdateAsync(id, request, ct), Messages.UpdatedSuccessfully);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _orders.DeleteAsync(id, ct);
        return Success(Messages.DeletedSuccessfully);
    }

    [HttpPost("{id:guid}/start"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> Start(Guid id, CancellationToken ct) => Success(await _orders.StartAsync(id, ct), "بدأ تنفيذ أمر العمل؛ المعدة في الصيانة.");

    [HttpPost("{id:guid}/complete"), RequireScreen(Screen, ScreenAction.Post)]
    public async Task<IActionResult> Complete(Guid id, [FromBody] CompleteEquipmentMaintenanceDto request, CancellationToken ct)
        => Success(await _orders.CompleteAsync(id, request, ct), "اكتمل أمر العمل وقُيِّدت تكلفته.");

    [HttpPost("{id:guid}/cancel"), RequireScreen(Screen, ScreenAction.Cancel)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] EquipmentReasonDto request, CancellationToken ct)
        => Success(await _orders.CancelAsync(id, request.Reason, ct), "تم إلغاء أمر العمل.");
}
