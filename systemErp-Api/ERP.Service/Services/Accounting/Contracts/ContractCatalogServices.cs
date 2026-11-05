using System.Text.RegularExpressions;
using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using A = ERP.Core.DTOs.Shared.ScreenAction;
using S = ERP.Core.Models.Shared.ContractStageDefinition;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// أنواع العقود للمنشأة. الرمز يُحفظ في العقود فلا يتغير بعد الإنشاء؛ طريقة الفوترة تُقرأ من النوع عند كل عملية فلا تتغير
/// لنوع عليه عقود (تنقلب قواعد فوترتها)؛ المسار وحد التبسيط يسريان على العقود الجديدة فقط (مسار العقد يُثبَّت عند إنشائه).
/// النوع المستخدم يُعطَّل ولا يُحذف.
/// </summary>
public partial class ContractTypeService : IContractTypeService
{
    private readonly ErpDbContext _db;
    private readonly IAuditService _audit;

    public ContractTypeService(ErpDbContext db, IAuditService audit)
    {
        _db = db; _audit = audit;
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{1,63}$")]
    private static partial Regex CodePattern();

    public async Task<List<ContractTypeDto>> ListAsync(bool activeOnly, CancellationToken ct = default)
    {
        var types = await _db.Set<ContractTypeDefinition>().AsNoTracking()
            .Where(t => !activeOnly || t.IsActive)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.NameAr).ToListAsync(ct);
        var counts = await _db.Set<CommercialContract>().AsNoTracking()
            .GroupBy(c => c.ContractType).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return types.Select(t => ToDto(t, counts.GetValueOrDefault(t.Code))).ToList();
    }

    public async Task<ContractTypeDto> CreateAsync(SaveContractTypeDto r, CancellationToken ct = default)
    {
        var code = (r.Code ?? string.Empty).Trim().ToLowerInvariant();
        var errors = Validate(r);
        if (!CodePattern().IsMatch(code)) errors.Insert(0, Messages.ContractTypeCodeInvalid);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
        if (await _db.Set<ContractTypeDefinition>().AnyAsync(t => t.Code == code, ct))
            throw new ConflictException(string.Format(Messages.ContractTypeCodeExists, code));

        var type = new ContractTypeDefinition { Code = code };
        Apply(type, r);
        _db.Add(type);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("CONTRACT_TYPE_CREATED", nameof(ContractTypeDefinition), type.Id.ToString(), Describe(type), ct);
        return ToDto(type, 0);
    }

    public async Task<ContractTypeDto> UpdateAsync(Guid id, SaveContractTypeDto r, CancellationToken ct = default)
    {
        var type = await _db.Set<ContractTypeDefinition>().FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(Messages.ContractTypeNotFound);
        var errors = Validate(r);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
        var used = await UsageAsync(type.Code, ct);
        if (used > 0 && r.Billing != type.Billing) throw new ConflictException(string.Format(Messages.ContractTypeBillingLocked, used));

        var before = Describe(type);
        Apply(type, r); // الرمز لا يتغير
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("CONTRACT_TYPE_UPDATED", nameof(ContractTypeDefinition), type.Id.ToString(), $"{before} ← {Describe(type)}", ct);
        return ToDto(type, used);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var type = await _db.Set<ContractTypeDefinition>().FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(Messages.ContractTypeNotFound);
        var used = await UsageAsync(type.Code, ct);
        if (used > 0) throw new ConflictException(string.Format(Messages.ContractTypeInUse, used));
        _db.Remove(type);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("CONTRACT_TYPE_DELETED", nameof(ContractTypeDefinition), type.Id.ToString(), Describe(type), ct);
    }

    private static List<string> Validate(SaveContractTypeDto r)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(r.NameAr)) errors.Add(Messages.ContractTypeNameRequired);
        if (!ContractWorkflows.IsValid(r.Workflow)) errors.Add(Messages.ContractTypeWorkflowInvalid);
        if (!ContractBilling.IsValid(r.Billing)) errors.Add(Messages.ContractTypeBillingInvalid);
        if (r.SimpleLimit is < 0) errors.Add(Messages.ContractTypeSimpleLimitInvalid);
        return errors;
    }

    private static void Apply(ContractTypeDefinition t, SaveContractTypeDto r)
    {
        t.NameAr = r.NameAr.Trim();
        t.NameEn = string.IsNullOrWhiteSpace(r.NameEn) ? t.NameAr : r.NameEn.Trim();
        t.Workflow = r.Workflow;
        t.Billing = r.Billing;
        // حد التبسيط لا معنى له لنوع مبسط أصلاً
        t.SimpleLimit = r.Workflow == ContractWorkflows.Simple || r.SimpleLimit is not > 0 ? null : r.SimpleLimit;
        t.RequiresGuarantee = r.RequiresGuarantee;
        t.IsActive = r.IsActive;
        t.SortOrder = r.SortOrder;
    }

    private Task<int> UsageAsync(string code, CancellationToken ct) => _db.Set<CommercialContract>().CountAsync(c => c.ContractType == code, ct);

    private static string Describe(ContractTypeDefinition t)
        => $"{t.Code} ({t.NameAr}): {t.Workflow}/{t.Billing}, حد {t.SimpleLimit?.ToString("0.##") ?? "—"}, ضمان {(t.RequiresGuarantee ? "نعم" : "لا")}, {(t.IsActive ? "فعّال" : "معطّل")}";

    private static ContractTypeDto ToDto(ContractTypeDefinition t, int count) => new()
    {
        Id = t.Id, Code = t.Code, NameAr = t.NameAr, NameEn = t.NameEn, Workflow = t.Workflow, Billing = t.Billing, SimpleLimit = t.SimpleLimit,
        RequiresGuarantee = t.RequiresGuarantee, IsActive = t.IsActive, SortOrder = t.SortOrder, ContractsCount = count,
    };
}

/// <summary>
/// كتالوج دورة العقود العام (جدولا ContractStageDefinition وContractTransition بلا منشأة): يديره مشرف المنصة ويسري على كل المنشآت.
/// الحالات: النصوص والمدة والإجراءات المسموحة فقط (المعرّف والرقم والمرحلة تعتمد عليها قواعد العمل). الانتقالات: كل الحقول،
/// ويُضاف انتقال جديد بين حالتين قائمتين؛ لا حذف (التعطيل يكفي ويحفظ سجل العقود). الإجراءات من إجراءات شاشة العقود،
/// والشرط من الشروط التي تعرفها دالة الانتقال. كل تعديل يُسجَّل في التدقيق.
/// </summary>
public class ContractWorkflowCatalogService : IContractWorkflowCatalogService
{
    private static readonly string[] Tones = { "primary", "danger", "neutral" };

    private readonly ErpDbContext _db;
    private readonly IAuditService _audit;

    public ContractWorkflowCatalogService(ErpDbContext db, IAuditService audit)
    {
        _db = db; _audit = audit;
    }

    private static A[] ScreenActions => PermissionCatalog.Find(PermissionCatalog.CommercialContractsScreenId)!.Actions;

    public async Task<ContractWorkflowCatalogDto> GetAsync(CancellationToken ct = default)
    {
        var stages = await _db.Set<S>().AsNoTracking().Where(s => s.WorkflowType == S.Workflow).OrderBy(s => s.Number).ToListAsync(ct);
        var transitions = await _db.Set<ContractTransition>().AsNoTracking().OrderBy(t => t.FromStatus).ThenBy(t => t.SortOrder).ToListAsync(ct);
        return new ContractWorkflowCatalogDto
        {
            Stages = stages.Select(ToDto).ToList(),
            Transitions = transitions.Select(ToDto).ToList(),
            Conditions = ContractTransitionConditions.All.ToList(),
            Actions = ScreenActions.Select(PermissionCatalog.Action)
                .Select(a => new ContractActionOptionDto { Code = a.Code, NameAr = a.NameAr, NameEn = a.NameEn }).ToList(),
        };
    }

    public async Task<ContractStageAdminDto> UpdateStageAsync(string stageId, SaveContractStageDto r, CancellationToken ct = default)
    {
        var stage = await _db.Set<S>().FirstOrDefaultAsync(s => s.Id == stageId && s.WorkflowType == S.Workflow, ct)
            ?? throw new NotFoundException(Messages.ContractStageNotFound);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(r.NameAr) || string.IsNullOrWhiteSpace(r.ShortNameAr)) errors.Add(Messages.ContractStageNameRequired);
        if (r.SlaDays is < 0 or > 3650) errors.Add(Messages.ContractStageSlaInvalid);
        var actions = ParseActions(r.AllowedActions, errors);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        var before = $"{stage.NameAr} [{stage.AllowedActions}] SLA {stage.SlaDays?.ToString() ?? "—"}";
        stage.NameAr = r.NameAr.Trim(); stage.NameEn = Text(r.NameEn, stage.NameAr);
        stage.ShortNameAr = r.ShortNameAr.Trim(); stage.ShortNameEn = Text(r.ShortNameEn, stage.ShortNameAr);
        stage.DescAr = (r.DescAr ?? string.Empty).Trim(); stage.DescEn = (r.DescEn ?? string.Empty).Trim();
        stage.SlaDays = r.SlaDays is > 0 ? r.SlaDays : null;
        stage.AllowedActions = actions;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("CONTRACT_STAGE_UPDATED", nameof(S), stage.Id,
            $"{before} ← {stage.NameAr} [{stage.AllowedActions}] SLA {stage.SlaDays?.ToString() ?? "—"}", ct);
        return ToDto(stage);
    }

    public async Task<ContractTransitionAdminDto> CreateTransitionAsync(SaveContractTransitionDto r, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var stages = await _db.Set<S>().AsNoTracking().Where(s => s.WorkflowType == S.Workflow).Select(s => s.Id).ToListAsync(ct);
        if (!stages.Contains(r.FromStatus) || !stages.Contains(r.ToStatus)) errors.Add(Messages.ContractTransitionStagesInvalid);
        else if (r.FromStatus == r.ToStatus) errors.Add(Messages.ContractTransitionSameStage);
        var transition = new ContractTransition { FromStatus = r.FromStatus, ToStatus = r.ToStatus };
        Apply(transition, r, errors);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
        // انتقال واحد لكل زوج حالتين (فهرس فريد)
        if (await _db.Set<ContractTransition>().AnyAsync(t => t.FromStatus == r.FromStatus && t.ToStatus == r.ToStatus, ct))
            throw new ConflictException(Messages.ContractTransitionExists);

        transition.Id = (await _db.Set<ContractTransition>().MaxAsync(t => (int?)t.Id, ct) ?? 0) + 1;
        _db.Add(transition);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("CONTRACT_TRANSITION_CREATED", nameof(ContractTransition), transition.Id.ToString(), Describe(transition), ct);
        return ToDto(transition);
    }

    public async Task<ContractTransitionAdminDto> UpdateTransitionAsync(int transitionId, SaveContractTransitionDto r, CancellationToken ct = default)
    {
        var transition = await _db.Set<ContractTransition>().FirstOrDefaultAsync(t => t.Id == transitionId, ct)
            ?? throw new NotFoundException(Messages.ContractTransitionNotFound);
        var errors = new List<string>();
        var before = Describe(transition);
        Apply(transition, r, errors); // الحالتان ثابتتان بعد الإضافة
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("CONTRACT_TRANSITION_UPDATED", nameof(ContractTransition), transition.Id.ToString(), $"{before} ← {Describe(transition)}", ct);
        return ToDto(transition);
    }

    private static void Apply(ContractTransition t, SaveContractTransitionDto r, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(r.NameAr)) errors.Add(Messages.ContractTransitionNameRequired);
        var actions = ParseActions(r.RequiredActions, errors);
        // انتقال بلا صلاحية يستطيعه كل من يرى العقد: يُرفض
        if (string.IsNullOrEmpty(actions)) errors.Add(Messages.ContractTransitionActionsRequired);
        var condition = string.IsNullOrWhiteSpace(r.Condition) ? null : r.Condition.Trim();
        if (condition != null && !ContractTransitionConditions.All.Contains(condition)) errors.Add(Messages.ContractTransitionConditionInvalid);
        var workflow = string.IsNullOrWhiteSpace(r.Workflow) ? null : r.Workflow.Trim();
        if (workflow != null && !ContractWorkflows.IsValid(workflow)) errors.Add(Messages.ContractTypeWorkflowInvalid);
        if (!Tones.Contains(r.Tone)) errors.Add(Messages.ContractTransitionToneInvalid);
        if (errors.Count > 0) return;

        t.NameAr = r.NameAr.Trim(); t.NameEn = Text(r.NameEn, t.NameAr);
        t.RequiredActions = actions; t.Condition = condition; t.Workflow = workflow;
        t.RequiresNote = r.RequiresNote; t.Tone = r.Tone; t.SortOrder = r.SortOrder; t.IsActive = r.IsActive;
    }

    /// <summary>رموز الإجراءات بصيغتها المحفوظة؛ رمز غير معروف أو ليس من إجراءات شاشة العقود يُرفض.</summary>
    private static string ParseActions(IEnumerable<string> codes, List<string> errors)
    {
        var parsed = new List<A>();
        foreach (var code in codes.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()))
        {
            if (PermissionCatalog.TryParse(code, out var a) && ScreenActions.Contains(a)) parsed.Add(a);
            else errors.Add(string.Format(Messages.ContractActionInvalid, code));
        }
        return PermissionStore.FormatActions(parsed);
    }

    private static string Text(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string Describe(ContractTransition t)
        => $"{t.FromStatus}→{t.ToStatus} ({t.NameAr}) [{t.RequiredActions}] شرط {t.Condition ?? "—"} مسار {t.Workflow ?? "الكل"}{(t.IsActive ? "" : " معطّل")}";

    private static ContractStageAdminDto ToDto(S s) => new()
    {
        Id = s.Id, Number = s.Number, NameAr = s.NameAr, NameEn = s.NameEn, ShortNameAr = s.ShortNameAr, ShortNameEn = s.ShortNameEn,
        DescAr = s.DescAr, DescEn = s.DescEn, Phase = s.Phase, SlaDays = s.SlaDays,
        AllowedActions = PermissionStore.ParseActions(s.AllowedActions).Select(PermissionCatalog.Code).ToList(),
    };

    private static ContractTransitionAdminDto ToDto(ContractTransition t) => new()
    {
        Id = t.Id, FromStatus = t.FromStatus, ToStatus = t.ToStatus, NameAr = t.NameAr, NameEn = t.NameEn,
        RequiredActions = PermissionStore.ParseActions(t.RequiredActions).Select(PermissionCatalog.Code).ToList(),
        Condition = t.Condition, Workflow = t.Workflow, RequiresNote = t.RequiresNote, Tone = t.Tone, SortOrder = t.SortOrder, IsActive = t.IsActive,
    };
}
