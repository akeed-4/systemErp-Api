using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

public class ApprovalPolicyService : CrudService<ApprovalPolicy, ApprovalPolicyDto, CreateApprovalPolicyDto, UpdateApprovalPolicyDto>, IApprovalPolicyService
{
    public ApprovalPolicyService(ErpDbContext db) : base(db) { }
    protected override string Label => Messages.LabelApprovalPolicy;

    protected override IQueryable<ApprovalPolicy> ApplySearch(IQueryable<ApprovalPolicy> q, string t)
        => q.Where(p => p.NameAr.Contains(t) || p.NameEn.Contains(t) || p.DocumentTypes.Contains(t));

    // المستندات تُخزَّن مفصولة بفاصلة وتُعرض قائمة
    protected override ApprovalPolicyDto ToDto(ApprovalPolicy entity)
    {
        var dto = base.ToDto(entity);
        dto.DocumentTypes = ApprovalDocumentCatalog.Split(entity.DocumentTypes);
        return dto;
    }

    protected override Task OnCreatingAsync(ApprovalPolicy entity, CreateApprovalPolicyDto dto, CancellationToken ct)
    {
        entity.DocumentTypes = ApprovalDocumentCatalog.Join(dto.DocumentTypes);
        return Task.CompletedTask;
    }

    protected override Task OnUpdatingAsync(ApprovalPolicy entity, UpdateApprovalPolicyDto dto, CancellationToken ct)
    {
        entity.DocumentTypes = ApprovalDocumentCatalog.Join(dto.DocumentTypes);
        return Task.CompletedTask;
    }

    protected override async Task ValidateAsync(CreateApprovalPolicyDto d, ApprovalPolicy? existing, CancellationToken ct)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(d.NameAr)) errors.Add(Messages.PolicyArabicNameRequired);
        d.DocumentTypes = d.DocumentTypes.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct().ToList();
        if (d.DocumentTypes.Count == 0 || string.IsNullOrWhiteSpace(d.ActionType)) errors.Add(Messages.DocumentTypeAndActionRequired);
        // كل مستند مختار يجب أن يكون في الفهرس، وأن يفرض السرفر اعتماد هذه العملية عليه (لا سياسة شكلية بلا أثر)
        foreach (var type in d.DocumentTypes)
        {
            var doc = ApprovalDocumentCatalog.Find(type);
            if (doc == null) errors.Add(string.Format(Messages.ApprovalDocumentUnknown, type));
            else if (!doc.Actions.Contains(d.ActionType)) errors.Add(string.Format(Messages.ApprovalActionNotSupported, doc.NameAr));
        }
        if (d.MinAmountTrigger < 0) errors.Add(Messages.AmountThresholdCannotBeNegative);
        if (d.Steps.Count == 0) errors.Add(Messages.PolicyNeedsOneLevel);
        var levels = d.Steps.Select(s => s.Level).OrderBy(l => l).ToList();
        if (!levels.SequenceEqual(Enumerable.Range(1, levels.Count))) errors.Add(Messages.ApprovalLevelsMustBeSequential);
        if (d.Steps.Any(s => string.IsNullOrWhiteSpace(s.ApproverRole) && s.ApproverUserId == null))
            errors.Add(Messages.LevelNeedsApprover);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        var userIds = d.Steps.Where(s => s.ApproverUserId.HasValue).Select(s => s.ApproverUserId!.Value).Distinct().ToList();
        if (userIds.Count > 0 && await Db.Set<User>().CountAsync(u => userIds.Contains(u.Id), ct) != userIds.Count)
            throw new ValidationFailedException(Messages.ApproverNotFound);
    }

    protected override async Task OnDeletingAsync(ApprovalPolicy e, CancellationToken ct)
    {
        if (await Db.Set<ApprovalRequest>().AnyAsync(r => r.PolicyId == e.Id, ct))
            throw new ConflictException(Messages.PolicyLinkedToRequests);
    }

    public async Task<ApprovalPolicyDto> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        var p = await Db.Set<ApprovalPolicy>().Include(x => x.Steps).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(Messages.PolicyNotFound);
        p.IsActive = isActive;
        await Db.SaveChangesAsync(ct);
        return Mapper.Map<ApprovalPolicyDto>(p);
    }
}
