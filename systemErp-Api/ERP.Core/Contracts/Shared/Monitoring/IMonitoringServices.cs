using DevExtreme.AspNet.Data.ResponseModel;
using ERP.Core.DTOs.Shared;
using ERP.Core.Models.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>
/// تسجيل أحداث المراقبة (الدخول، أخطاء الخادم، محاولات الإرسال للهيئة) في سياق مستقل عن معاملة العملية. لا يرمي أبداً:
/// فشل التسجيل يُكتب في سجل الخادم ولا يوقف العملية الأصلية.
/// </summary>
public interface IMonitoringRecorder
{
    /// <summary>في القاعدة المشتركة؛ عنوان الجهاز ومتصفحه من الطلب الحالي.</summary>
    Task RecordLoginAsync(LoginEvent entry, CancellationToken ct = default);
    /// <summary>في القاعدة المشتركة.</summary>
    Task RecordApiErrorAsync(ApiErrorLog entry, CancellationToken ct = default);
    /// <summary>في قاعدة المنشأة الحالية (المشتركة أو المستقلة).</summary>
    Task RecordZatcaAttemptAsync(ZatcaSubmissionAttempt entry, CancellationToken ct = default);
}

/// <summary>قراءات مركز مراقبة المنشأة (للقراءة فقط، ومنشأة المستخدم وحدها).</summary>
public interface ICommandCenterService
{
    /// <summary>المؤشرات المالية والتجارية للفترة (week | month | year).</summary>
    Task<CommandCenterOverviewDto> OverviewAsync(string? period, CancellationToken ct = default);
    /// <summary>النبض التشغيلي الخفيف للتحديث الدوري.</summary>
    Task<CommandCenterLiveDto> LiveAsync(CancellationToken ct = default);
    Task<LoadResult> ZatcaAttemptsAsync(DataSourceLoadOptions options, DateTime? from, DateTime? to, CancellationToken ct = default);
    Task<LoadResult> AuditActivityAsync(DataSourceLoadOptions options, DateTime? from, DateTime? to, CancellationToken ct = default);
    Task<LoadResult> LoginEventsAsync(DataSourceLoadOptions options, DateTime? from, DateTime? to, CancellationToken ct = default);
    Task<List<InventoryAlertRowDto>> InventoryAlertsAsync(CancellationToken ct = default);
}

/// <summary>فحوص المنشأة اليدوية وتنبيهاتها ودورة معالجتها.</summary>
public interface IMonitoringAlertService
{
    Task<MonitoringCheckRunDto> RunChecksAsync(CancellationToken ct = default);
    Task<MonitoringCheckRunDto?> LatestRunAsync(CancellationToken ct = default);
    /// <summary>status: active (مفتوح/مُقرّ/قيد المعالجة، الافتراضي) | all | open | acknowledged | in_progress | resolved | closed.</summary>
    Task<LoadResult> LoadAlertsAsync(DataSourceLoadOptions options, string? status, CancellationToken ct = default);
    Task<MonitoringAlertDetailsDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<MonitoringAlertDetailsDto> ActAsync(Guid id, MonitoringAlertActionRequestDto request, CancellationToken ct = default);
}

/// <summary>مراقبة المنصة (مدير المنصة): لا تقرأ بيانات أعمال المنشآت.</summary>
public interface IPlatformMonitoringService
{
    Task<PlatformMonitoringOverviewDto> OverviewAsync(CancellationToken ct = default);
    Task<LoadResult> ApiErrorsAsync(DataSourceLoadOptions options, DateTime? from, DateTime? to, CancellationToken ct = default);
    Task<LoadResult> LoginEventsAsync(DataSourceLoadOptions options, DateTime? from, DateTime? to, CancellationToken ct = default);
    Task<PlatformCheckRunDto> RunChecksAsync(CancellationToken ct = default);
    Task<PlatformCheckRunDto?> LatestRunAsync(CancellationToken ct = default);
}
