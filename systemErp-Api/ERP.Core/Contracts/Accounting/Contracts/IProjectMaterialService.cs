using ERP.Core.DTOs.Accounting.Contracts;

namespace ERP.Core.Contracts.Accounting;

/// <summary>
/// احتياجات مواد المشروع (عقد مقاولات بجدول كميات): خطة واحدة لكل عقد، تُشتق من تحليل بنود المكتبة أو تُدخل يدوياً، ثم تُقدَّم
/// وتُعتمد فتصبح الأساس المعتمد لمقارنة طلبات الشراء. الصلاحيات ونطاق السجل يفحصها المتحكم على سجل العقد.
/// </summary>
public interface IProjectMaterialService
{
    /// <summary>الخطة بأسطرها وأرصدتها؛ وإن لم تُنشأ بعد فمسودة فارغة.</summary>
    Task<ProjectMaterialPlanDto> GetAsync(Guid contractId, CancellationToken ct = default);
    /// <summary>الاحتياجات المقترحة من جدول الكميات وتحليل بنود المكتبة (لا تُحفظ).</summary>
    Task<DerivedProjectMaterialsDto> DeriveAsync(Guid contractId, CancellationToken ct = default);
    /// <summary>حفظ المسودة (الأسطر كاملة).</summary>
    Task<ProjectMaterialPlanDto> SaveAsync(Guid contractId, SaveProjectMaterialPlanDto request, CancellationToken ct = default);
    Task<ProjectMaterialPlanDto> SubmitAsync(Guid contractId, CancellationToken ct = default);
    /// <summary>اعتماد المقدَّمة: تصبح الأساس المعتمد ولا تُعدَّل بعده.</summary>
    Task<ProjectMaterialPlanDto> ApproveAsync(Guid contractId, CancellationToken ct = default);
    /// <summary>إعادة المقدَّمة للمسودة بسبب إلزامي.</summary>
    Task<ProjectMaterialPlanDto> ReturnAsync(Guid contractId, ProjectMaterialPlanActionDto request, CancellationToken ct = default);
}
