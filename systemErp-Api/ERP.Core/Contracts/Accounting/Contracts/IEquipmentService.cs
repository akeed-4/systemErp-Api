using ERP.Core.DTOs.Accounting.Contracts;

namespace ERP.Core.Contracts.Accounting;

/// <summary>
/// سجل المعدات والآليات وتشغيلها: السجل (مملوكة بأصلها الثابت، ومستأجرة بمؤجرها)، والإسناد للمشاريع بالمشغّل والتحويل والإرجاع،
/// وسجل التشغيل (ساعات، وقود، مصاريف، توقف)، وعقود الإيجار، واللوحة وتقرير التكلفة. تكلفة الساعة تُحمَّل على المشروع من التقرير
/// اليومي عبر مورد المشروع الذي ينشئه الإسناد، فلا قيد مكرر هنا.
/// </summary>
public interface IEquipmentService
{
    // السجل
    Task<List<EquipmentDto>> ListAsync(CancellationToken ct = default);
    Task<EquipmentDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<EquipmentDto> CreateAsync(SaveEquipmentDto request, CancellationToken ct = default);
    Task<EquipmentDto> UpdateAsync(Guid id, SaveEquipmentDto request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<EquipmentOptionsDto> OptionsAsync(CancellationToken ct = default);

    // الإسناد
    Task<List<EquipmentAssignmentDto>> AssignmentsAsync(Guid? equipmentId, Guid? contractId, bool activeOnly, CancellationToken ct = default);
    Task<EquipmentAssignmentDto> AssignAsync(AssignEquipmentDto request, CancellationToken ct = default);
    Task<EquipmentAssignmentDto> UpdateAssignmentAsync(Guid assignmentId, UpdateEquipmentAssignmentDto request, CancellationToken ct = default);
    Task<EquipmentAssignmentDto> ReturnAsync(Guid assignmentId, ReturnEquipmentDto request, CancellationToken ct = default);
    Task<EquipmentAssignmentDto> TransferAsync(Guid assignmentId, TransferEquipmentDto request, CancellationToken ct = default);

    // سجل التشغيل
    Task<List<EquipmentLogDto>> LogsAsync(Guid? equipmentId, string? kind, DateTime? from, DateTime? to, CancellationToken ct = default);
    Task<EquipmentLogDto> AddLogAsync(SaveEquipmentLogDto request, CancellationToken ct = default);
    Task DeleteLogAsync(Guid logId, CancellationToken ct = default);

    // عقود الإيجار
    Task<List<EquipmentRentalContractDto>> RentalsAsync(Guid? equipmentId, CancellationToken ct = default);
    Task<EquipmentRentalContractDto> CreateRentalAsync(SaveEquipmentRentalContractDto request, CancellationToken ct = default);
    Task<EquipmentRentalContractDto> UpdateRentalAsync(Guid rentalId, SaveEquipmentRentalContractDto request, CancellationToken ct = default);
    Task<EquipmentRentalContractDto> ActivateRentalAsync(Guid rentalId, CancellationToken ct = default);
    Task<EquipmentRentalContractDto> CloseRentalAsync(Guid rentalId, CancellationToken ct = default);
    Task DeleteRentalAsync(Guid rentalId, CancellationToken ct = default);

    // اللوحة والتقارير
    Task<EquipmentDashboardDto> DashboardAsync(CancellationToken ct = default);
    Task<EquipmentCostReportDto> CostReportAsync(DateTime? from, DateTime? to, CancellationToken ct = default);
}

/// <summary>
/// أوامر عمل الصيانة (وقائية وتصحيحية) بقطع غيارها: مفتوح ← قيد التنفيذ ← مكتمل، أو ملغى. الإكمال يصرف القطع من الموقع بتكلفتها
/// الفعلية ويقيد مصروف الصيانة، والإلغاء بعد الإكمال يعكس الحركات والقيد.
/// </summary>
public interface IEquipmentMaintenanceService
{
    Task<List<EquipmentMaintenanceOrderDto>> ListAsync(Guid? equipmentId, string? status, CancellationToken ct = default);
    Task<EquipmentMaintenanceOrderDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<EquipmentMaintenanceOrderDto> CreateAsync(SaveEquipmentMaintenanceOrderDto request, CancellationToken ct = default);
    Task<EquipmentMaintenanceOrderDto> UpdateAsync(Guid id, SaveEquipmentMaintenanceOrderDto request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<EquipmentMaintenanceOrderDto> StartAsync(Guid id, CancellationToken ct = default);
    Task<EquipmentMaintenanceOrderDto> CompleteAsync(Guid id, CompleteEquipmentMaintenanceDto request, CancellationToken ct = default);
    Task<EquipmentMaintenanceOrderDto> CancelAsync(Guid id, string? reason, CancellationToken ct = default);
}

/// <summary>
/// إيرادات المعدات وربحيتها: تسعيرات الإيراد (افتراضية للمعدة أو خاصة بعقد، بأساس ساعة/يوم/وردية/رحلة/مهمة)، وتقرير يقارن الإيراد
/// التشغيلي المحسوب من الاستخدام والإيراد المحاسبي المعتمد في المستخلصات بالتكلفة الفعلية لكل معدة ولكل مشروع. حساب بلا قيود.
/// </summary>
public interface IEquipmentProfitabilityService
{
    Task<List<EquipmentRevenueRateDto>> RatesAsync(Guid? equipmentId, Guid? contractId, CancellationToken ct = default);
    Task<EquipmentRevenueRateDto> CreateRateAsync(SaveEquipmentRevenueRateDto request, CancellationToken ct = default);
    Task<EquipmentRevenueRateDto> UpdateRateAsync(Guid id, SaveEquipmentRevenueRateDto request, CancellationToken ct = default);
    Task DeleteRateAsync(Guid id, CancellationToken ct = default);
    Task<List<EquipmentRevenueItemOptionDto>> ItemOptionsAsync(Guid contractId, CancellationToken ct = default);
    Task<EquipmentProfitabilityReportDto> ReportAsync(DateTime? from, DateTime? to, Guid? contractId, CancellationToken ct = default);
}
