namespace ERP.Core.DTOs.Accounting.Contracts;

/// <summary>إعداد مشروع المقاولات (العقد هو المشروع): بياناته من العقد، وملفه، وفترات العمل، ومراحله، وأعضاؤه.</summary>
public class ConstructionProjectDto
{
    public Guid ContractId { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? ProjectName { get; set; }
    public string? ProjectLocation { get; set; }
    public string PartyName { get; set; } = string.Empty;
    public string? ConsultantName { get; set; }
    public string Stage { get; set; } = string.Empty;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal ContractValue { get; set; }
    public Guid? CostCenterId { get; set; }
    public string? CostCenterCode { get; set; }
    public string? CostCenterName { get; set; }

    public decimal EstimatedBudget { get; set; }
    /// <summary>بداية ونهاية يوم العمل والاستراحة بصيغة HH:mm.</summary>
    public string WorkdayStart { get; set; } = string.Empty;
    public string WorkdayEnd { get; set; } = string.Empty;
    public string? BreakStart { get; set; }
    public string? BreakEnd { get; set; }
    public string? Notes { get; set; }
    /// <summary>فترات الإنجاز المولّدة من ساعات العمل (ساعة لكل فترة، والأخيرة قد تقصر).</summary>
    public List<ProjectHourSlotDto> HourSlots { get; set; } = new();

    public List<ProjectPhaseDto> Phases { get; set; } = new();
    public decimal PhaseWeightTotal { get; set; }
    /// <summary>نسبة الإنجاز المخططة حتى اليوم من أوزان المراحل وتواريخها.</summary>
    public decimal PlannedProgressPercent { get; set; }
    public List<ProjectMemberDto> Members { get; set; } = new();
}

public class ProjectHourSlotDto
{
    public string Start { get; set; } = string.Empty;
    public string End { get; set; } = string.Empty;
    public decimal Hours { get; set; }
}

public class ProjectPhaseDto
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime PlannedStart { get; set; }
    public DateTime PlannedEnd { get; set; }
    public decimal WeightPercent { get; set; }
    public string? Notes { get; set; }
    public List<Guid> ContractItemIds { get; set; } = new();
    /// <summary>للقراءة: النسبة المخططة من وزن المرحلة حتى اليوم (0–100).</summary>
    public decimal PlannedCompletionPercent { get; set; }
}

public class ProjectMemberDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Role { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class SaveProjectProfileDto
{
    public decimal EstimatedBudget { get; set; }
    public string WorkdayStart { get; set; } = "07:00";
    public string WorkdayEnd { get; set; } = "17:00";
    public string? BreakStart { get; set; }
    public string? BreakEnd { get; set; }
    public string? Notes { get; set; }
}

/// <summary>المراحل كاملة (تستبدل المحفوظ).</summary>
public class SaveProjectPhasesDto
{
    public List<ProjectPhaseDto> Phases { get; set; } = new();
}

public class SaveProjectMemberDto
{
    public Guid UserId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

/// <summary>مستخدم فعّال يمكن إسناده للمشروع.</summary>
public class ProjectMemberCandidateDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
}

/// <summary>نتيجة إنشاء أدوار المقاولات الجاهزة: المنشأة، والموجودة مسبقاً باسمها.</summary>
public class ConstructionRoleTemplatesResultDto
{
    public List<string> Created { get; set; } = new();
    public List<string> Existing { get; set; } = new();
}
