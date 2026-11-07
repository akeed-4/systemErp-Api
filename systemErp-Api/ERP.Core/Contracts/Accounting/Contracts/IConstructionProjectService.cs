using ERP.Core.DTOs.Accounting.Contracts;

namespace ERP.Core.Contracts.Accounting;

/// <summary>
/// إعداد مشروع المقاولات (عقد بجدول كميات؛ العقد هو المشروع): ملف المشروع وساعات عمله، ومراحل التنفيذ، وأعضاؤه.
/// الصلاحيات ونطاق سجل العقد يفحصها المتحكم.
/// </summary>
public interface IConstructionProjectService
{
    /// <summary>إعداد المشروع؛ العقد القديم بلا ملف يُعرض بالقيم الافتراضية حتى يُحفظ.</summary>
    Task<ConstructionProjectDto> GetAsync(Guid contractId, CancellationToken ct = default);
    /// <summary>يحفظ الملف، وينشئ مركز تكلفة المشروع إن لم يكن للعقد مركز.</summary>
    Task<ConstructionProjectDto> SaveProfileAsync(Guid contractId, SaveProjectProfileDto request, CancellationToken ct = default);
    Task<ConstructionProjectDto> SavePhasesAsync(Guid contractId, SaveProjectPhasesDto request, CancellationToken ct = default);
    Task<ConstructionProjectDto> AddMemberAsync(Guid contractId, SaveProjectMemberDto request, CancellationToken ct = default);
    Task<ConstructionProjectDto> UpdateMemberAsync(Guid contractId, Guid memberId, SaveProjectMemberDto request, CancellationToken ct = default);
    Task<ConstructionProjectDto> RemoveMemberAsync(Guid contractId, Guid memberId, CancellationToken ct = default);
    Task<List<ProjectMemberCandidateDto>> MemberCandidatesAsync(CancellationToken ct = default);
    /// <summary>موارد المشروع بتكلفة ساعتها (الآليات المملوكة والمستأجرة، المهندسون، العمالة).</summary>
    Task<List<ProjectResourceDto>> ResourcesAsync(Guid contractId, CancellationToken ct = default);
    /// <summary>إضافة (بلا معرّف) أو تعديل مورد؛ تكلفة الساعة يحسبها الخادم. يعيد القائمة.</summary>
    Task<List<ProjectResourceDto>> SaveResourceAsync(Guid contractId, Guid? resourceId, ProjectResourceDto request, CancellationToken ct = default);
    /// <summary>حذف مورد لم يُستخدم في تقرير يومي (المستخدم يُعطَّل).</summary>
    Task<List<ProjectResourceDto>> DeleteResourceAsync(Guid contractId, Guid resourceId, CancellationToken ct = default);
    /// <summary>خطة الإنجاز اليومية: المستهدف بالساعة لكل بند وتاريخ سريانه.</summary>
    Task<List<ProjectDailyPlanDto>> DailyPlansAsync(Guid contractId, CancellationToken ct = default);
    Task<List<ProjectDailyPlanDto>> SaveDailyPlansAsync(Guid contractId, SaveProjectDailyPlansDto request, CancellationToken ct = default);
    /// <summary>ينشئ أدوار المقاولات الجاهزة (مدير المشروع، المهندس المشرف، ...) بصلاحياتها؛ الموجود باسمه يُترك كما هو.</summary>
    Task<ConstructionRoleTemplatesResultDto> CreateRoleTemplatesAsync(CancellationToken ct = default);
}
