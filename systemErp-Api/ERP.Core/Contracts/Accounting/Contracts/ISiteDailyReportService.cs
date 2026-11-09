using ERP.Core.DTOs.Accounting.Contracts;

namespace ERP.Core.Contracts.Accounting;

/// <summary>
/// التقرير اليومي للموقع (إنجاز المشروع بالساعة): المشرف يسجّل ويقدّم، والمهندس المسؤول يعتمد أو يعيد. الحسابات (الإنتاج،
/// نسبة التحقيق، التوقف، تكلفة الساعة والوحدة، أسباب الانخفاض) في الخادم، والتكلفة تشغيلية لا قيد لها.
/// الصلاحيات ونطاق سجل العقد يفحصها المتحكم.
/// </summary>
public interface ISiteDailyReportService
{
    /// <summary>أسطر القياس في جدول كميات المشروع (للتقرير وحده: لا تحتاج صلاحية العقود).</summary>
    Task<List<SiteReportBoqLineDto>> BoqLinesAsync(Guid contractId, CancellationToken ct = default);
    Task<List<SiteReportListItemDto>> ListAsync(Guid contractId, DateTime? from, DateTime? to, CancellationToken ct = default);
    /// <summary>تقرير اليوم إن وُجد، وإلا قالب جديد من فترات ساعات العمل وخطة الإنجاز (بلا معرّف).</summary>
    Task<SiteDailyReportDto> DayAsync(Guid contractId, DateTime date, CancellationToken ct = default);
    Task<SiteDailyReportDto> GetAsync(Guid contractId, Guid reportId, CancellationToken ct = default);
    Task<SiteDailyReportDto> CreateAsync(Guid contractId, SaveSiteDailyReportDto request, CancellationToken ct = default);
    Task<SiteDailyReportDto> UpdateAsync(Guid contractId, Guid reportId, SaveSiteDailyReportDto request, CancellationToken ct = default);
    Task DeleteAsync(Guid contractId, Guid reportId, CancellationToken ct = default);
    Task<SiteDailyReportDto> SubmitAsync(Guid contractId, Guid reportId, CancellationToken ct = default);
    Task<SiteDailyReportDto> ApproveAsync(Guid contractId, Guid reportId, CancellationToken ct = default);
    /// <summary>قيد تحميل العمالة والمعدات للتقارير المعتمدة التي لم يُرحَّل تحميلها (اعتُمدت قبل التفعيل)؛ يعيد عددها.</summary>
    Task<int> PostPendingCostsAsync(Guid contractId, CancellationToken ct = default);
    /// <summary>إعادة المقدَّم للمسودة بسبب إلزامي.</summary>
    Task<SiteDailyReportDto> ReturnAsync(Guid contractId, Guid reportId, SiteReportActionDto request, CancellationToken ct = default);

    /// <summary>تقرير الإنجاز بالساعة: كل فترة بمستهدفها ومنفّذها وتوقفها.</summary>
    Task<List<SiteHourlyRowDto>> HourlyAsync(Guid contractId, SiteReportFilterDto filter, CancellationToken ct = default);
    /// <summary>تقرير الإنتاجية اليومية: لكل يوم وبند المنفّذ ونسبة التحقيق والتكلفة وتكلفة الوحدة.</summary>
    Task<List<SiteProductivityRowDto>> ProductivityAsync(Guid contractId, SiteReportFilterDto filter, CancellationToken ct = default);
    /// <summary>ساعات الموارد وتكلفتها لكل يوم (الآليات والمهندسون والعمال).</summary>
    Task<List<SiteResourceRowDto>> ResourceUsageAsync(Guid contractId, SiteReportFilterDto filter, CancellationToken ct = default);
}
