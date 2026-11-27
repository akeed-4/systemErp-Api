using ERP.Core.DTOs.Accounting;

namespace ERP.Core.Contracts.Accounting;

/// <summary>إدارة قوالب الدليل والأنشطة (مدير المنصة؛ على القاعدة المشتركة وحدها).</summary>
public interface IChartTemplateService
{
    Task<List<BusinessActivityDto>> ListActivitiesAsync(bool activeOnly, CancellationToken ct = default);
    Task<BusinessActivityDto> SaveActivityAsync(BusinessActivityDto dto, CancellationToken ct = default);

    Task<List<ChartTemplateSummaryDto>> ListTemplatesAsync(CancellationToken ct = default);
    Task<ChartTemplateDto> GetTemplateAsync(Guid id, CancellationToken ct = default);
    Task<ChartTemplateDto> CreateTemplateAsync(SaveChartTemplateDto dto, CancellationToken ct = default);
    Task<ChartTemplateDto> UpdateTemplateAsync(Guid id, SaveChartTemplateDto dto, CancellationToken ct = default);
    Task DeleteTemplateAsync(Guid id, CancellationToken ct = default);

    /// <summary>الشجرة الناتجة لنشاط وموديولات على شجرة فارغة (منشأة جديدة)، مع أخطاء الدمج.</summary>
    Task<ChartCompositionDto> PreviewAsync(ChartCompositionRequestDto request, CancellationToken ct = default);
    /// <summary>المفاتيح الوظيفية المعروفة للنظام (للاختيار في محرر القوالب).</summary>
    List<AccountSemanticKeyDto> SemanticKeys();
}

/// <summary>دليل المنشأة الحالية من القوالب: الحالة، ومعاينة الناقص وتطبيقه، والربط الوظيفي.</summary>
public interface IChartSetupService
{
    Task<ChartSetupStatusDto> GetStatusAsync(CancellationToken ct = default);
    /// <summary>ما سيُنشأ أو يُربط بلا كتابة.</summary>
    Task<ChartSetupReportDto> PreviewAsync(ApplyChartSetupDto request, CancellationToken ct = default);
    /// <summary>ينشئ الناقص فقط ويربط المفاتيح الفارغة في معاملة واحدة؛ لا يعدّل حساباً قائماً ولا يحذفه.</summary>
    Task<ChartSetupReportDto> ApplyAsync(ApplyChartSetupDto request, CancellationToken ct = default);
    /// <summary>حسابات الموديولات غير المرخّصة التي لم تُستخدم (مرشّحة للإيقاف بعد التحقق؛ لا يُوقف شيء تلقائياً).</summary>
    Task<List<ChartSetupItemDto>> UnusedModuleAccountsAsync(CancellationToken ct = default);

    Task<List<AccountBindingDto>> ListBindingsAsync(CancellationToken ct = default);
    Task<AccountBindingDto> SaveBindingAsync(SaveAccountBindingDto dto, CancellationToken ct = default);
}

/// <summary>
/// حل المفتاح الوظيفي إلى رقم حساب المنشأة الحالية (للخدمات الجديدة): ذاكرة مؤقتة لكل منشأة، تُحمَّل باستعلام واحد وتُبطَل عند تغيير الربط.
/// لا يقرأ القوالب. null = المفتاح غير مربوط ولا افتراضي له.
/// </summary>
public interface IAccountKeyResolver
{
    Task<string?> ResolveAsync(string semanticKey, CancellationToken ct = default);
}
