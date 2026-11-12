using ERP.Core.DTOs.Accounting.Contracts;

namespace ERP.Core.Contracts.Accounting;

/// <summary>
/// خطة تنفيذ المشروع من دراسة الجدوى المجمّدة: التوليد (المراحل والمواد والعمالة والمعدات والميزانية والتواريخ المقترحة)،
/// والمراجعة والتعديل، والتقديم والاعتماد (بنسختي الدراسة وأساس الخطة المجمّدتين) والإعادة، والمراجعة بعد الاعتماد.
/// </summary>
public interface IExecutionPlanService
{
    Task<ExecutionPlanDto> GetAsync(Guid contractId, CancellationToken ct = default);

    Task<ExecutionPlanDto> GenerateAsync(Guid contractId, GenerateExecutionPlanDto request, CancellationToken ct = default);

    Task<ExecutionPlanDto> SaveResourcesAsync(Guid contractId, SaveResourceRequirementsDto request, CancellationToken ct = default);

    Task<ExecutionPlanDto> SubmitAsync(Guid contractId, CancellationToken ct = default);

    Task<ExecutionPlanDto> ApproveAsync(Guid contractId, CancellationToken ct = default);

    Task<ExecutionPlanDto> ReturnAsync(Guid contractId, ReturnExecutionPlanDto request, CancellationToken ct = default);

    /// <summary>مراجعة بعد الاعتماد: مسودة برقم مراجعة أعلى؛ النسخ المعتمدة السابقة تبقى كما هي.</summary>
    Task<ExecutionPlanDto> ReviseAsync(Guid contractId, CancellationToken ct = default);

    /// <summary>الدراسة الأصلية ↔ الأساس المعتمد ↔ الخطة الحالية ↔ الفعلي من المستندات المعتمدة، لكل فئة ومرحلة.</summary>
    Task<ExecutionPlanComparisonDto> ComparisonAsync(Guid contractId, CancellationToken ct = default);
}
