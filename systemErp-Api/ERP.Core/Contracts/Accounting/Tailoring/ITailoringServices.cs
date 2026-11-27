using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Accounting;

/// <summary>إعداد الخياطة: الإعداد العام، وأنواع الملابس بقوالب قياسها وتصميمها، ومراحل الإنتاج. أول قراءة تزرع قالب الثوب السعودي.</summary>
public interface ITailoringSetupService
{
    Task<TailoringSettingsDto> SettingsAsync(CancellationToken ct = default);
    Task<TailoringSettingsDto> SaveSettingsAsync(SaveTailoringSettingsDto request, CancellationToken ct = default);

    Task<List<TailoringGarmentTypeDto>> GarmentTypesAsync(bool includeInactive, CancellationToken ct = default);
    Task<TailoringGarmentTypeDto> GarmentTypeAsync(Guid id, CancellationToken ct = default);
    Task<TailoringGarmentTypeDto> CreateGarmentTypeAsync(SaveTailoringGarmentTypeDto request, CancellationToken ct = default);
    Task<TailoringGarmentTypeDto> UpdateGarmentTypeAsync(Guid id, SaveTailoringGarmentTypeDto request, CancellationToken ct = default);
    Task DeleteGarmentTypeAsync(Guid id, CancellationToken ct = default);
    /// <summary>يستكمل قالب الثوب بالحقول والخيارات الافتراضية الناقصة حسب المفتاح (THOBE-M فقط).</summary>
    Task<TailoringGarmentTypeDto> CompleteDefaultsAsync(Guid id, CancellationToken ct = default);

    Task<List<TailoringStageDto>> StagesAsync(CancellationToken ct = default);
    Task<TailoringStageDto> CreateStageAsync(SaveTailoringStageDto request, CancellationToken ct = default);
    Task<TailoringStageDto> UpdateStageAsync(Guid id, SaveTailoringStageDto request, CancellationToken ct = default);
    Task DeleteStageAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// أمر التفصيل: المسودة (عرض السعر) وتسعيرها في الخادم، والتأكيد بإنشاء القطع، والدفعة المقدمة بفاتورة ضريبية على حساب الالتزام
/// وإلغاؤها بمرتجع نظامي، والإلغاء والإرجاع للمسودة.
/// </summary>
public interface ITailoringOrderService
{
    Task<DevExtreme.AspNet.Data.ResponseModel.LoadResult> LoadAsync(DataSourceLoadOptions options, CancellationToken ct = default);
    Task<TailoringOrderDto> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>تسعير الطلب بلا حفظ (الأرقام نفسها التي يحفظها الإنشاء).</summary>
    Task<TailoringOrderDto> PreviewAsync(SaveTailoringOrderDto request, Guid? id, CancellationToken ct = default);
    Task<TailoringOrderDto> CreateAsync(SaveTailoringOrderDto request, CancellationToken ct = default);
    Task<TailoringOrderDto> UpdateAsync(Guid id, SaveTailoringOrderDto request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<TailoringOrderDto> ConfirmAsync(Guid id, CancellationToken ct = default);
    Task<TailoringOrderDto> ReopenAsync(Guid id, TailoringReasonDto request, CancellationToken ct = default);
    Task<TailoringOrderDto> CancelAsync(Guid id, TailoringReasonDto request, CancellationToken ct = default);
    Task<TailoringOrderDto> AdvanceAsync(Guid id, TailoringAdvanceRequestDto request, CancellationToken ct = default);
    Task<TailoringOrderDto> CancelAdvanceAsync(Guid id, TailoringReasonDto request, CancellationToken ct = default);
}

/// <summary>ملف العميل للخياطة وملفات القياس ونسخها الثابتة ومقارنتها. العميل نفسه من سجل العملاء القائم.</summary>
public interface ITailoringCustomerService
{
    Task<DevExtreme.AspNet.Data.ResponseModel.LoadResult> LoadAsync(DataSourceLoadOptions options, CancellationToken ct = default);
    Task<TailoringCustomerDto> GetAsync(Guid customerId, CancellationToken ct = default);
    Task<TailoringCustomerDto> SaveProfileAsync(Guid customerId, SaveTailoringCustomerProfileDto request, CancellationToken ct = default);

    Task<TailoringMeasurementProfileDto> CreateMeasurementProfileAsync(Guid customerId, SaveTailoringMeasurementProfileDto request, CancellationToken ct = default);
    Task<TailoringMeasurementProfileDto> UpdateMeasurementProfileAsync(Guid profileId, SaveTailoringMeasurementProfileDto request, CancellationToken ct = default);
    Task DeleteMeasurementProfileAsync(Guid profileId, CancellationToken ct = default);

    Task<List<TailoringMeasurementVersionDto>> VersionsAsync(Guid profileId, CancellationToken ct = default);
    Task<TailoringMeasurementVersionDto> AddVersionAsync(Guid profileId, CreateTailoringMeasurementVersionDto request, CancellationToken ct = default);
    Task<TailoringMeasurementCompareDto> CompareAsync(Guid fromVersionId, Guid toVersionId, CancellationToken ct = default);
}

/// <summary>
/// الإنتاج: لوحة القطع بالمراحل، والإسناد، ونقل المراحل بسجلها ومدتها، و«جاهزة» بعد المراحل الإلزامية والجودة، والبروفات وطلبات
/// التعديل. حالة الأمر تُشتق من حالات قطعه بعد كل تغيير.
/// </summary>
public interface ITailoringProductionService
{
    Task<TailoringBoardDto> BoardAsync(CancellationToken ct = default);
    Task AssignAsync(TailoringAssignDto request, CancellationToken ct = default);
    Task MoveAsync(Guid garmentId, TailoringMoveDto request, CancellationToken ct = default);
    Task<List<TailoringStageHistoryDto>> HistoryAsync(Guid garmentId, CancellationToken ct = default);

    Task<List<TailoringFittingDto>> FittingsAsync(Guid? orderId, bool upcomingOnly, CancellationToken ct = default);
    Task<TailoringFittingDto> ScheduleFittingAsync(SaveTailoringFittingDto request, CancellationToken ct = default);
    Task<TailoringFittingDto> RecordFittingAsync(Guid fittingId, TailoringFittingResultDto request, CancellationToken ct = default);

    Task<List<TailoringModificationDto>> ModificationsAsync(Guid? orderId, bool openOnly, CancellationToken ct = default);
    Task<TailoringModificationDto> CreateModificationAsync(SaveTailoringModificationDto request, CancellationToken ct = default);
    Task<TailoringModificationDto> CompleteModificationAsync(Guid id, CancellationToken ct = default);
    Task<TailoringModificationDto> CancelModificationAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// التكلفة الفعلية: صرف الأقمشة والمواد وإرجاعها (حركة مخزون + قيد تحت التنفيذ)، وأجور القطعة (آلية من الإنتاج أو يدوية، وإلغاؤها بقيد
/// عكسي)، وأجور الخياطين الخاصة، والربحية الفعلية للأمر. التكلفة تُعرض بصلاحية رؤية التكلفة فقط.
/// </summary>
public interface ITailoringCostingService
{
    Task<List<TailoringMaterialDto>> MaterialsAsync(Guid? orderId, DateTime? from, DateTime? to, CancellationToken ct = default);
    Task<TailoringMaterialDto> IssueAsync(IssueTailoringMaterialDto request, CancellationToken ct = default);
    Task<TailoringMaterialDto> ReturnAsync(ReturnTailoringMaterialDto request, CancellationToken ct = default);

    Task<List<TailoringLaborDto>> LaborAsync(Guid? orderId, Guid? employeeId, DateTime? from, DateTime? to, CancellationToken ct = default);
    Task<TailoringLaborDto> AddLaborAsync(SaveTailoringLaborDto request, CancellationToken ct = default);
    Task<TailoringLaborDto> CancelLaborAsync(Guid id, string? reason, CancellationToken ct = default);

    Task<List<TailoringWorkerSummaryDto>> WorkersAsync(DateTime? from, DateTime? to, CancellationToken ct = default);
    Task SaveWorkerRatesAsync(Guid employeeId, SaveTailoringWorkerRatesDto request, CancellationToken ct = default);

    Task<TailoringProfitabilityDto> ProfitabilityAsync(Guid orderId, CancellationToken ct = default);
}

/// <summary>
/// الجودة والتسليم: فحص القطع واعتمادها أو رفضها، وتسليم القطع الجاهزة (جزئياً أو كاملاً) بتوقيع المستلم وفاتورة صافيها ناقص نصيبها من
/// الدفعة المقدمة ونقل تكلفتها، وإلغاء التسليم بعكس آثاره، وإغلاق الأمر بعد التسليم الكامل.
/// </summary>
public interface ITailoringDeliveryService
{
    Task InspectAsync(TailoringInspectDto request, CancellationToken ct = default);
    Task<TailoringOrderDeliveryDto> OrderAsync(Guid orderId, CancellationToken ct = default);
    Task<List<TailoringDeliveryDto>> ListAsync(Guid? orderId, DateTime? from, DateTime? to, CancellationToken ct = default);
    Task<TailoringDeliveryDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<TailoringDeliveryAmountsDto> PreviewAsync(TailoringDeliveryPreviewRequestDto request, CancellationToken ct = default);
    Task<TailoringDeliveryDto> DeliverAsync(DeliverTailoringDto request, CancellationToken ct = default);
    Task<TailoringDeliveryDto> CancelAsync(Guid id, TailoringReasonDto request, CancellationToken ct = default);
    Task<TailoringOrderDeliveryDto> CloseOrderAsync(Guid orderId, CancellationToken ct = default);
}

/// <summary>تنبيهات الخياطة داخل التطبيق: جاهزية الأمر للتسليم، وملخص يومي للمتأخر والمستحق اليوم ولبروفات اليوم. تعذّرها لا يُسقط العملية.</summary>
public interface ITailoringAlertService
{
    Task OrderReadyAsync(Guid orderId, CancellationToken ct = default);
    Task ScanAsync(CancellationToken ct = default);
}

/// <summary>لوحة الخياطة (وتشغّل الفحص اليومي للتنبيهات).</summary>
public interface ITailoringDashboardService
{
    Task<TailoringDashboardDto> DashboardAsync(CancellationToken ct = default);
}

/// <summary>تقارير الخياطة الاثنا عشر بمفتاحها وفترتها (الربحية والأجور تتطلبان رؤية التكلفة).</summary>
public interface ITailoringReportService
{
    Task<TailoringReportDto> ReportAsync(string key, DateTime? from, DateTime? to, CancellationToken ct = default);
}
