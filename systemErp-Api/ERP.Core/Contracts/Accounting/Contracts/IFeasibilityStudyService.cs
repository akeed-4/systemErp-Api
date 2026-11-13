using ERP.Core.DTOs.Accounting.Contracts;

namespace ERP.Core.Contracts.Accounting;

/// <summary>دراسات الجدوى (السجل نفسه قبل أن يصير عقداً): سجلها، و«إنشاء العقد» من الدراسة والخطة المعتمدتين.</summary>
public interface IFeasibilityStudyService
{
    /// <summary>الدراسات في نطاق وصول المستخدم إلى العقود؛ <paramref name="status"/> = study | plan_approved | contracted | فارغ للكل.</summary>
    Task<List<FeasibilityStudyRowDto>> ListAsync(string? status, CancellationToken ct = default);

    /// <summary>
    /// «إنشاء العقد»: الدراسة بخطة معتمدة تصير عقداً بالسجل نفسه (رقم عقد، ومركز تكلفة المشروع) ويُفعَّل مباشرة (ساري) بلا دورة
    /// المراجعة والاعتماد، بشرط بيانات العقد المكتملة والضمان وصلاحية الاعتماد؛ وإلا يُرفض كله بما ينقص. رقم الدراسة ونسختاها تبقى.
    /// </summary>
    Task<FeasibilityStudyRowDto> CreateContractAsync(Guid contractId, CreateContractFromStudyDto request, CancellationToken ct = default);

    /// <summary>تفعيل عقد أُنشئ من دراسة وبقي مسودة (قبل قاعدة التفعيل المباشر، أو بعد استكمال شروطه).</summary>
    Task<FeasibilityStudyRowDto> ActivateContractAsync(Guid contractId, CancellationToken ct = default);
}
