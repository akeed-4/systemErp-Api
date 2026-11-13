using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// دراسات الجدوى: السجل نفسه (عقد المقاولات) بمرحلة <see cref="CommercialContract.FeasibilityStatus"/>؛ لا مستند موازٍ.
/// السجل من مجموعة العقود العامة فيسري نطاق الوصول إلى العقود.
/// </summary>
public class FeasibilityStudyService : IFeasibilityStudyService
{
    private readonly ErpDbContext _db;
    private readonly ITransactionRunner _tx;
    private readonly INumberSequenceService _numbers;
    private readonly ICurrentUser _user;
    private readonly IContractWorkflowService _workflow;

    public FeasibilityStudyService(ErpDbContext db, ITransactionRunner tx, INumberSequenceService numbers, ICurrentUser user, IContractWorkflowService workflow)
    {
        _db = db; _tx = tx; _numbers = numbers; _user = user; _workflow = workflow;
    }

    public async Task<FeasibilityStudyRowDto> ActivateContractAsync(Guid contractId, CancellationToken ct = default)
    {
        await _tx.RunAsync(async token =>
        {
            await _workflow.ActivateFromStudyAsync(contractId, token);
            await _db.SaveChangesAsync(token);
        }, ct);
        return (await ListAsync(null, ct)).First(r => r.Id == contractId);
    }

    public async Task<FeasibilityStudyRowDto> CreateContractAsync(Guid contractId, CreateContractFromStudyDto request, CancellationToken ct = default)
    {
        await _tx.RunAsync(async token =>
        {
            var c = await _db.Set<CommercialContract>().FirstOrDefaultAsync(x => x.Id == contractId, token)
                ?? throw new NotFoundException(Messages.ContractNotFound);
            if (c.FeasibilityStatus != FeasibilityStatuses.PlanApproved || c.Stage != ContractStageDefinition.Draft)
                throw new ConflictException(Messages.FeasibilityCreateContractNeedsApprovedPlan);
            var plan = await _db.Set<ProjectExecutionPlan>().AsNoTracking().FirstOrDefaultAsync(p => p.ContractId == contractId, token);
            if (plan?.Status != ExecutionPlanStatuses.Approved) throw new ConflictException(Messages.FeasibilityCreateContractNeedsApprovedPlan);

            // رقم العقد من ترقيم العقود (الدراسة احتفظت برقمها في FeasibilityNumber)، ثم مركز تكلفة المشروع باسمه
            c.ContractNumber = await _numbers.AssignAsync(new DocumentNumberRequest
            {
                Key = "commercial_contract", DefaultPrefix = "CON-", WarehouseId = c.WarehouseId, Date = c.StartDate,
                ManualNumber = string.IsNullOrWhiteSpace(request.ContractNumber) ? null : request.ContractNumber.Trim(), UserEntered = true,
                Exists = (n, x) => _db.Set<CommercialContract>().AnyAsync(k => k.ContractNumber == n && k.Id != contractId, x),
            }, token);
            c.FeasibilityStatus = FeasibilityStatuses.Contracted;
            await ConstructionProjectService.EnsureCostCenterAsync(_db, c, token);
            // الموافقة المالية للجولة الأولى من اعتماد الدراسة نفسها (معتمِدها وتاريخها ومرجعها)
            await FeasibilityFinancialApproval.GrantAsync(_db, c, plan, _user, token);
            await _db.SaveChangesAsync(token);
            // اعتماد الدراسة والخطة هو قرار التعاقد: العقد يُفعَّل مباشرة بلا مراجعة واعتماد ثانيين؛ ما ينقص يرفض الإنشاء كله
            await _workflow.ActivateFromStudyAsync(contractId, token);
            await _db.SaveChangesAsync(token);
        }, ct);
        return (await ListAsync(null, ct)).First(r => r.Id == contractId);
    }

    public async Task<List<FeasibilityStudyRowDto>> ListAsync(string? status, CancellationToken ct = default)
    {
        var q = _db.Set<CommercialContract>().AsNoTracking().Where(c => c.FeasibilityStatus != null);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(c => c.FeasibilityStatus == status);
        var rows = await q.OrderByDescending(c => c.CreatedAt).Select(c => new FeasibilityStudyRowDto
        {
            Id = c.Id, FeasibilityNumber = c.FeasibilityNumber ?? string.Empty, FeasibilityStatus = c.FeasibilityStatus!,
            ContractNumber = c.FeasibilityStatus == FeasibilityStatuses.Contracted ? c.ContractNumber : null, Stage = c.Stage, Title = c.Title,
            ProjectName = c.ProjectName, ProjectLocation = c.ProjectLocation, PartyName = c.PartyName, StartDate = c.StartDate, EndDate = c.EndDate,
            ContractValue = c.ContractValue, EstimatedCost = c.EstimatedCost, CreatedAt = c.CreatedAt,
            LineCount = c.Items.Count(i => !i.IsGroup),
        }).ToListAsync(ct);
        if (rows.Count == 0) return rows;

        var ids = rows.Select(r => r.Id).ToList();
        var priced = await _db.Set<ContractItemResourceCost>().AsNoTracking().Where(r => ids.Contains(r.ContractId))
            .GroupBy(r => r.ContractId).Select(g => new { g.Key, Lines = g.Select(r => r.ContractItemId).Distinct().Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Lines, ct);
        var plans = await FeasibilityPlanStatus.LoadAsync(_db, ids, ct);
        foreach (var r in rows)
        {
            r.Margin = DocumentPricing.Round(r.ContractValue - r.EstimatedCost);
            r.MarginPercent = r.ContractValue > 0 ? Math.Round(r.Margin / r.ContractValue * 100m, 2) : 0;
            r.PricedLineCount = priced.GetValueOrDefault(r.Id);
            r.PlanStatus = plans.GetValueOrDefault(r.Id);
        }
        return rows;
    }
}

/// <summary>حالة خطة التنفيذ لكل دراسة.</summary>
internal static class FeasibilityPlanStatus
{
    public static async Task<Dictionary<Guid, string?>> LoadAsync(ErpDbContext db, List<Guid> contractIds, CancellationToken ct)
        => await db.Set<ProjectExecutionPlan>().AsNoTracking().Where(p => contractIds.Contains(p.ContractId))
            .ToDictionaryAsync(p => p.ContractId, p => (string?)p.Status, ct);
}
