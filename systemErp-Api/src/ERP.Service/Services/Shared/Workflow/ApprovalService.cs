using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

public class ApprovalService : IApprovalService
{
    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;
    private readonly ITransactionRunner _tx;

    public ApprovalService(ErpDbContext db, ICurrentUser user, ITransactionRunner tx)
    {
        _db = db; _user = user; _tx = tx;
    }

    public Task<ApprovalCheckResultDto> CheckAndCreateAsync(ApprovalCheckRequestDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var me = _user.UserId ?? throw new UnauthorizedAppException();
            if (string.IsNullOrWhiteSpace(r.DocumentType) || string.IsNullOrWhiteSpace(r.ActionType))
                throw new ValidationFailedException("نوع المستند والإجراء مطلوبان.");

            var policy = (await _db.Set<ApprovalPolicy>().Include(p => p.Steps)
                    .Where(p => p.IsActive && p.DocumentType == r.DocumentType && p.ActionType == r.ActionType).ToListAsync(token))
                .Where(p => p.MinAmountTrigger == null || r.DocumentAmount >= p.MinAmountTrigger)
                .OrderByDescending(p => p.MinAmountTrigger ?? 0).FirstOrDefault();
            if (policy == null || policy.Steps.Count == 0) return new ApprovalCheckResultDto { ApprovalRequired = false };

            var pending = await _db.Set<ApprovalRequest>().Include(x => x.History)
                .FirstOrDefaultAsync(x => x.DocumentId == r.DocumentId && x.ActionType == r.ActionType && x.Status == "pending", token);
            if (pending != null)
                return new ApprovalCheckResultDto { ApprovalRequired = true, Request = Mapper.Map<ApprovalRequestDto>(pending) };

            var first = policy.Steps.OrderBy(s => s.Level).First();
            var request = new ApprovalRequest
            {
                PolicyId = policy.Id, PolicyNameAr = policy.NameAr, DocumentType = r.DocumentType, DocumentId = r.DocumentId,
                DocumentNumber = r.DocumentNumber, ActionType = r.ActionType, DocumentAmount = r.DocumentAmount,
                RequesterUserId = me, RequesterName = _user.Name ?? string.Empty,
                CurrentLevel = 1, TotalLevels = policy.Steps.Count, Status = "pending",
                RequiredApproverRole = first.ApproverRole, RequiredApproverUserId = first.ApproverUserId,
            };
            _db.Add(request);
            await _db.SaveChangesAsync(token);
            await NotifyApproversAsync(request, token);
            return new ApprovalCheckResultDto { ApprovalRequired = true, Request = Mapper.Map<ApprovalRequestDto>(request) };
        }, ct);

    public async Task<PagedResult<ApprovalRequestDto>> ListRequestsAsync(string? status, PaginationParams p, CancellationToken ct = default)
    {
        var q = _db.Set<ApprovalRequest>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var t = p.SearchTerm.Trim();
            q = q.Where(x => x.DocumentNumber.Contains(t) || x.RequesterName.Contains(t) || x.PolicyNameAr.Contains(t));
        }
        var total = await q.CountAsync(ct);
        var items = await q.Include(x => x.History).OrderByDescending(x => x.CreatedAt)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);
        return new PagedResult<ApprovalRequestDto>
        {
            Items = items.Select(Mapper.Map<ApprovalRequestDto>).ToList(),
            TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    public async Task<ApprovalRequestDto> GetAsync(Guid id, CancellationToken ct = default)
        => Mapper.Map<ApprovalRequestDto>(await _db.Set<ApprovalRequest>().AsNoTracking().Include(x => x.History).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("طلب الاعتماد غير موجود"));

    public Task<ApprovalRequestDto> ApproveAsync(Guid id, ApprovalDecisionDto d, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var (request, me) = await LoadForDecisionAsync(id, token);
            request.History.Add(new ApprovalHistoryItem
            {
                Level = request.CurrentLevel, ApproverUserId = me, ApproverName = _user.Name ?? string.Empty,
                Status = "approved", Comment = d.Comment, Timestamp = DateTime.UtcNow,
            });

            if (request.CurrentLevel < request.TotalLevels)
            {
                var next = await _db.Set<ApprovalPolicyStep>().AsNoTracking()
                    .FirstOrDefaultAsync(s => s.PolicyId == request.PolicyId && s.Level == request.CurrentLevel + 1, token);
                request.CurrentLevel++;
                request.RequiredApproverRole = next?.ApproverRole;
                request.RequiredApproverUserId = next?.ApproverUserId;
                await _db.SaveChangesAsync(token);
                await NotifyApproversAsync(request, token);
            }
            else
            {
                request.Status = "approved";
                await _db.SaveChangesAsync(token);
                await NotifyRequesterAsync(request, "approval_approved", "تم اعتماد طلبك", token);
            }
            return Mapper.Map<ApprovalRequestDto>(request);
        }, ct);

    public async Task<ApprovalRequestDto> CancelAsync(Guid id, CancellationToken ct = default)
    {
        var me = _user.UserId ?? throw new UnauthorizedAppException();
        var request = await _db.Set<ApprovalRequest>().Include(x => x.History).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("طلب الاعتماد غير موجود");
        if (request.Status != "pending") throw new ConflictException("لا يُسحب إلا الطلب المعلّق.");
        if (request.RequesterUserId != me && _user.RoleId is not ("owner" or "admin")) throw new ForbiddenException("لا يسحب الطلب إلا مقدّمه أو المالك/المدير.");
        request.Status = "cancelled";
        await _db.SaveChangesAsync(ct);
        return Mapper.Map<ApprovalRequestDto>(request);
    }

    public Task<ApprovalRequestDto> RejectAsync(Guid id, ApprovalDecisionDto d, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            if (string.IsNullOrWhiteSpace(d.Comment)) throw new ValidationFailedException("سبب الرفض مطلوب.");
            var (request, me) = await LoadForDecisionAsync(id, token);
            request.History.Add(new ApprovalHistoryItem
            {
                Level = request.CurrentLevel, ApproverUserId = me, ApproverName = _user.Name ?? string.Empty,
                Status = "rejected", Comment = d.Comment, Timestamp = DateTime.UtcNow,
            });
            request.Status = "rejected";
            request.RejectionReason = d.Comment;
            await _db.SaveChangesAsync(token);
            await NotifyRequesterAsync(request, "approval_rejected", "تم رفض طلبك", token);
            return Mapper.Map<ApprovalRequestDto>(request);
        }, ct);

    private async Task<(ApprovalRequest Request, Guid Me)> LoadForDecisionAsync(Guid id, CancellationToken ct)
    {
        var me = _user.UserId ?? throw new UnauthorizedAppException();
        var request = await _db.Set<ApprovalRequest>().Include(x => x.History).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("طلب الاعتماد غير موجود");
        if (request.Status != "pending") throw new ConflictException("الطلب تم البت فيه مسبقاً.");

        var role = _user.RoleId;
        var isOwner = role == "owner";
        var allowed = isOwner
            || (request.RequiredApproverUserId.HasValue && request.RequiredApproverUserId == me)
            || (request.RequiredApproverUserId == null && request.RequiredApproverRole != null && request.RequiredApproverRole == role);
        if (!allowed) throw new ForbiddenException("لست المعتمد المطلوب في هذا المستوى.");
        if (request.RequesterUserId == me && !isOwner) throw new ForbiddenException("لا يمكنك اعتماد طلبك بنفسك.");
        return (request, me);
    }

    private async Task NotifyApproversAsync(ApprovalRequest r, CancellationToken ct)
    {
        var recipients = r.RequiredApproverUserId.HasValue
            ? await _db.Set<User>().Where(u => u.Id == r.RequiredApproverUserId && u.IsActive).ToListAsync(ct)
            : (await _db.Set<User>().Where(u => u.IsActive).ToListAsync(ct)).Where(u => RoleIds.From(u.Role) == r.RequiredApproverRole).ToList();
        foreach (var u in recipients)
            _db.Add(new AppNotification
            {
                RecipientUserId = u.Id, RecipientRole = r.RequiredApproverRole, Type = "approval_request",
                Title = "طلب اعتماد جديد", Body = $"{r.PolicyNameAr}: {r.DocumentNumber} بمبلغ {r.DocumentAmount:0.00} (المستوى {r.CurrentLevel}/{r.TotalLevels})",
                RelatedDocType = r.DocumentType, RelatedDocId = r.DocumentId, RelatedDocNumber = r.DocumentNumber, ApprovalRequestId = r.Id,
            });
        await _db.SaveChangesAsync(ct);
    }

    private async Task NotifyRequesterAsync(ApprovalRequest r, string type, string title, CancellationToken ct)
    {
        _db.Add(new AppNotification
        {
            RecipientUserId = r.RequesterUserId, Type = type, Title = title,
            Body = $"{r.PolicyNameAr}: {r.DocumentNumber}" + (r.RejectionReason != null ? $" - {r.RejectionReason}" : ""),
            RelatedDocType = r.DocumentType, RelatedDocId = r.DocumentId, RelatedDocNumber = r.DocumentNumber, ApprovalRequestId = r.Id,
        });
        await _db.SaveChangesAsync(ct);
    }
}
