using ERP.Core.DTOs.Accounting.Contracts;

namespace ERP.Core.Contracts.Accounting;

/// <summary>دراسات الجدوى (السجل نفسه قبل أن يصير عقداً): سجلها، و«إنشاء العقد» من الدراسة والخطة المعتمدتين.</summary>
public interface IFeasibilityStudyService
{
    /// <summary>الدراسات في نطاق وصول المستخدم إلى العقود؛ <paramref name="status"/> = study | plan_approved | contracted | فارغ للكل.</summary>
    Task<List<FeasibilityStudyRowDto>> ListAsync(string? status, CancellationToken ct = default);

    /// <summary>
    /// «إنشاء العقد»: الدراسة بخطة معتمدة تصير عقداً بالسجل نفسه (رقم عقد، ومركز تكلفة المشروع)، وتدخل دورة العقد من المسودة.
    /// رقم الدراسة ونسختاها المجمّدتان تبقى. يعيد صف الدراسة المحدَّث.
    /// </summary>
    Task<FeasibilityStudyRowDto> CreateContractAsync(Guid contractId, CreateContractFromStudyDto request, CancellationToken ct = default);
}
