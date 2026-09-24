using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

public class ApprovalPolicyService : CrudService<ApprovalPolicy, ApprovalPolicyDto, CreateApprovalPolicyDto, UpdateApprovalPolicyDto>, IApprovalPolicyService
{
    public ApprovalPolicyService(ErpDbContext db) : base(db) { }
    protected override string Label => "سياسة الاعتماد";

    protected override IQueryable<ApprovalPolicy> ApplySearch(IQueryable<ApprovalPolicy> q, string t)
        => q.Where(p => p.NameAr.Contains(t) || p.NameEn.Contains(t) || p.DocumentType.Contains(t));

    protected override async Task ValidateAsync(CreateApprovalPolicyDto d, ApprovalPolicy? existing, CancellationToken ct)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(d.NameAr)) errors.Add("اسم السياسة بالعربية مطلوب.");
        if (string.IsNullOrWhiteSpace(d.DocumentType) || string.IsNullOrWhiteSpace(d.ActionType)) errors.Add("نوع المستند والإجراء مطلوبان.");
        if (d.MinAmountTrigger < 0) errors.Add("حد المبلغ لا يكون سالباً.");
        if (d.Steps.Count == 0) errors.Add("السياسة تحتاج مستوى اعتماد واحداً على الأقل.");
        var levels = d.Steps.Select(s => s.Level).OrderBy(l => l).ToList();
        if (!levels.SequenceEqual(Enumerable.Range(1, levels.Count))) errors.Add("مستويات الاعتماد يجب أن تكون متسلسلة تبدأ من 1.");
        if (d.Steps.Any(s => string.IsNullOrWhiteSpace(s.ApproverRole) && s.ApproverUserId == null))
            errors.Add("كل مستوى يحتاج دور معتمد أو مستخدماً معتمداً.");
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        var userIds = d.Steps.Where(s => s.ApproverUserId.HasValue).Select(s => s.ApproverUserId!.Value).Distinct().ToList();
        if (userIds.Count > 0 && await Db.Set<User>().CountAsync(u => userIds.Contains(u.Id), ct) != userIds.Count)
            throw new ValidationFailedException("أحد المعتمدين غير موجود.");
    }

    protected override async Task OnDeletingAsync(ApprovalPolicy e, CancellationToken ct)
    {
        if (await Db.Set<ApprovalRequest>().AnyAsync(r => r.PolicyId == e.Id, ct))
            throw new ConflictException("السياسة مرتبطة بطلبات اعتماد؛ عطّلها بدل حذفها.");
    }

    public async Task<ApprovalPolicyDto> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        var p = await Db.Set<ApprovalPolicy>().Include(x => x.Steps).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("السياسة غير موجودة");
        p.IsActive = isActive;
        await Db.SaveChangesAsync(ct);
        return Mapper.Map<ApprovalPolicyDto>(p);
    }
}
