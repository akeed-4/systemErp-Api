using ERP.Core.Contracts.Shared;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// الموافقة المالية الآلية لعقد جاء من دراسة جدوى معتمدة: سجل <see cref="ContractApproval"/> عادي (مالي، معتمد) في جولة المراجعة
/// الحالية عند «إنشاء العقد»، معتمِده من اعتمد خطة الدراسة وتاريخه تاريخ ذلك الاعتماد ومرجعه رقم الدراسة ونسختها المجمّدة.
/// لا يحتاج صلاحية المراجعة المالية لأنه قرار الدراسة نفسه لا قرار مستخدم على العقد. يُسقط (ويبقى للتدقيق) إذا عاد العقد للمسودة
/// أو اختلفت قيمته عن قيمة الدراسة المعتمدة، فتلزم المراجعة المالية العادية. شرط «موافقة الاثنين» في دورة العقد يقرأه كأي موافقة.
/// </summary>
internal static class FeasibilityFinancialApproval
{
    private const string FinancialKind = "financial";

    /// <summary>
    /// يُمنح مرة واحدة عند «إنشاء العقد» إن كانت قيمة العقد هي قيمة الدراسة المجمّدة عند اعتماد الخطة. لا يحفظ (يحفظه المستدعي في معاملته).
    /// </summary>
    public static async Task GrantAsync(ErpDbContext db, CommercialContract c, ProjectExecutionPlan plan, ICurrentUser user, CancellationToken ct)
    {
        if (plan.Status != ExecutionPlanStatuses.Approved || plan.ApprovedAt == null) return;
        if (await db.Set<ContractApproval>().AnyAsync(a => a.ContractId == c.Id && a.Source == ContractApprovalSources.FeasibilityStudy, ct)) return;

        var snapshot = await db.Set<ProjectSnapshot>().AsNoTracking()
            .Where(s => s.ContractId == c.Id && s.Kind == ProjectSnapshotKinds.Feasibility && s.Revision == plan.Revision)
            .Select(s => new { s.Id, s.ContractValue }).FirstOrDefaultAsync(ct);
        if (snapshot == null) return;
        if (!SameValue(snapshot.ContractValue, c.ContractValue))
        {
            Audit(db, user, "CONTRACT_FINANCIAL_AUTO_APPROVAL_SKIPPED", c,
                $"{c.ContractNumber} | الدراسة {c.FeasibilityNumber}: قيمة العقد {c.ContractValue:0.##} تختلف عن قيمة الدراسة المعتمدة {snapshot.ContractValue:0.##}؛ تلزم المراجعة المالية");
            return;
        }

        var approverName = string.IsNullOrWhiteSpace(plan.ApprovedBy) ? "system" : plan.ApprovedBy;
        db.Add(new ContractApproval
        {
            ContractId = c.Id, Round = c.ReviewRound, Kind = FinancialKind, Decision = "approved",
            UserId = plan.ApprovedByUserId, UserName = approverName, DecidedAt = plan.ApprovedAt.Value,
            Note = $"موافقة مالية آلية من دراسة الجدوى المعتمدة {c.FeasibilityNumber}",
            Source = ContractApprovalSources.FeasibilityStudy, SourceReference = c.FeasibilityNumber, SourceId = snapshot.Id,
            ApprovedValue = snapshot.ContractValue,
        });
        Audit(db, user, "CONTRACT_FINANCIAL_AUTO_APPROVED", c,
            $"{c.ContractNumber} | الدراسة {c.FeasibilityNumber} | المعتمِد {approverName} في {plan.ApprovedAt:yyyy-MM-dd HH:mm} | القيمة {snapshot.ContractValue:0.##} | الجولة {c.ReviewRound}");
    }

    /// <summary>الموافقة الآلية تُحتسب ما دامت قائمة وقيمة العقد هي ما غطّته؛ المراجعات اليدوية كما هي.</summary>
    public static bool IsEffective(ContractApproval a, decimal contractValue)
        => a.Source != ContractApprovalSources.FeasibilityStudy
           || (a.InvalidatedAt == null && a.ApprovedValue is { } value && SameValue(value, contractValue));

    /// <summary>الرجوع للمسودة: تُسقط الموافقة الآلية في الجولة المنتهية (الجولة الجديدة مراجعة عادية). لا يحفظ.</summary>
    public static Task InvalidateOnDraftResetAsync(ErpDbContext db, CommercialContract c, ICurrentUser user, CancellationToken ct)
        => InvalidateAsync(db, c, user, ContractApprovalSources.DraftReset, _ => true, ct);

    /// <summary>تعديل العقد: تُسقط الموافقة الآلية إن اختلفت قيمته عمّا غطّته. لا يحفظ.</summary>
    public static Task InvalidateOnValueChangeAsync(ErpDbContext db, CommercialContract c, ICurrentUser user, CancellationToken ct)
        => InvalidateAsync(db, c, user, ContractApprovalSources.ValueChanged, a => a.ApprovedValue is not { } v || !SameValue(v, c.ContractValue), ct);

    private static async Task InvalidateAsync(ErpDbContext db, CommercialContract c, ICurrentUser user, string reason,
        Func<ContractApproval, bool> applies, CancellationToken ct)
    {
        if (c.FeasibilityStatus == null) return; // العقد المباشر لا موافقة آلية له
        var active = await db.Set<ContractApproval>()
            .Where(a => a.ContractId == c.Id && a.Source == ContractApprovalSources.FeasibilityStudy && a.InvalidatedAt == null)
            .ToListAsync(ct);
        foreach (var a in active.Where(applies))
        {
            a.InvalidatedAt = DateTime.UtcNow;
            a.InvalidationReason = reason;
            Audit(db, user, "CONTRACT_FINANCIAL_AUTO_APPROVAL_INVALIDATED", c,
                $"{c.ContractNumber} | الدراسة {a.SourceReference} | السبب {reason} | القيمة المعتمدة {a.ApprovedValue:0.##} ← الحالية {c.ContractValue:0.##} | الجولة {a.Round}");
        }
    }

    private static bool SameValue(decimal a, decimal b) => Math.Abs(Math.Round(a, 2) - Math.Round(b, 2)) < 0.005m;

    /// <summary>سجل التدقيق العام نفسه (AuditLog) يُضاف في معاملة العملية بدل حفظ منفصل.</summary>
    private static void Audit(ErpDbContext db, ICurrentUser user, string action, CommercialContract c, string details)
        => db.Add(new AuditLog
        {
            Action = action, EntityName = nameof(CommercialContract), EntityId = c.Id.ToString(), PerformedBy = user.Name ?? "system",
            Details = details.Length > 1000 ? details[..1000] : details, Timestamp = DateTime.UtcNow,
        });
}
