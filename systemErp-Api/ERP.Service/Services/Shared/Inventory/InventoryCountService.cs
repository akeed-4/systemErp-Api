using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// مستندات الجرد: التعديل للمسودة والمرفوض فقط، ورصيد النظام والتكلفة والفروق يحسبها السرفر عند كل حفظ
/// ثم تُجمَّد عند الإرسال. الإرسال ينشئ مستند اعتماد مستقلاً، والتسوية لا تتم إلا باعتماده.
/// </summary>
public class InventoryCountService : CrudService<InventoryCount, InventoryCountDto, CreateInventoryCountDto, UpdateInventoryCountDto>, IInventoryCountService
{
    public const string DocumentType = "inventory_count";
    public const string ApprovalAction = "approve";

    private readonly INumberSequenceService _numbers;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;
    private readonly IApprovalService _approvals;

    public InventoryCountService(ErpDbContext db, INumberSequenceService numbers, ICurrentUser user, IAuditService audit, IApprovalService approvals) : base(db)
    {
        _numbers = numbers; _user = user; _audit = audit; _approvals = approvals;
    }

    protected override string Label => Messages.LabelInventoryCount;
    protected override bool Transactional => true;

    public async Task<PagedResult<InventoryCountDto>> ListAsync(InventoryCountScope? scope, PaginationParams p, CancellationToken ct = default)
    {
        var q = Db.Set<InventoryCount>().AsNoTracking();
        if (scope.HasValue) q = q.Where(c => c.Scope == scope);
        if (TryParseStatus(p.Status, out var status)) q = q.Where(c => c.Status == status);
        if (p.StartDate.HasValue) q = q.Where(c => c.CountDate >= p.StartDate);
        if (p.EndDate.HasValue) q = q.Where(c => c.CountDate <= p.EndDate);
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var t = p.SearchTerm.Trim();
            q = q.Where(c => c.CountNumber.Contains(t) || c.CountedBy.Contains(t) || (c.WarehouseName != null && c.WarehouseName.Contains(t)) || (c.Location != null && c.Location.Contains(t)));
        }
        var total = await q.CountAsync(ct);
        // القائمة بلا أسطر: التفاصيل عبر GetAsync
        var items = await q.OrderByDescending(c => c.CountDate).ThenByDescending(c => c.CreatedAt)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);
        return new PagedResult<InventoryCountDto>
        {
            Items = items.Select(ToDto).ToList(), TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    public override Task<PagedResult<InventoryCountDto>> ListAsync(PaginationParams p, CancellationToken ct = default) => ListAsync(null, p, ct);

    private static bool TryParseStatus(string? value, out InventoryCountStatus status)
    {
        status = default;
        return !string.IsNullOrWhiteSpace(value) && Enum.TryParse(value.Replace("_", ""), true, out status);
    }

    public Task<List<InventoryCountLineDto>> SnapshotAsync(InventoryCountSnapshotRequestDto request, CancellationToken ct = default)
        => InventoryCountLines.SnapshotAsync(Db, request, ct);

    // ---------------- الإدخال والتعديل ----------------
    protected override async Task ValidateAsync(CreateInventoryCountDto d, InventoryCount? existing, CancellationToken ct)
    {
        if (existing != null)
        {
            if (existing.Status is not (InventoryCountStatus.Draft or InventoryCountStatus.Rejected))
                throw new ConflictException(Messages.OnlyDraftOrRejectedCountEditable);
            if (existing.Scope != d.Scope) throw new ValidationFailedException(Messages.CannotChangeCountScope);
        }

        var errors = new List<string>();
        if (!Enum.IsDefined(d.Scope)) errors.Add(Messages.CountScopeValues);
        if (!Enum.IsDefined(d.CountType)) errors.Add(Messages.CountTypeValues);
        if (d.CountDate > DateTime.UtcNow.AddDays(1)) errors.Add(Messages.CountDateInFuture);
        if (d.Lines.Count == 0) errors.Add(Messages.AtLeastOneCountLineRequired);

        if (d.Scope == InventoryCountScope.Items)
        {
            if (d.Lines.Any(l => l.ItemId is null || l.ItemId == Guid.Empty)) errors.Add(Messages.ItemCountLineRequiresItem);
            if (d.Lines.Any(l => !string.IsNullOrWhiteSpace(l.ChassisNumber))) errors.Add(Messages.ItemCountNoVins);
            if (d.Lines.Where(l => l.ItemId.HasValue).GroupBy(l => l.ItemId).Any(g => g.Count() > 1)) errors.Add(Messages.DuplicateItemInCount);
            if (d.Lines.Any(l => l.CountedQuantity < 0)) errors.Add(Messages.CountedQtyCannotBeNegative);
        }
        else
        {
            if (d.Lines.Any(l => string.IsNullOrWhiteSpace(l.ChassisNumber))) errors.Add(Messages.VehicleCountLineRequiresVin);
            if (d.Lines.Any(l => l.ItemId.HasValue)) errors.Add(Messages.VehicleCountNoItems);
            if (d.Lines.Where(l => !string.IsNullOrWhiteSpace(l.ChassisNumber)).GroupBy(l => InventoryCountLines.NormalizeVin(l.ChassisNumber)).Any(g => g.Count() > 1))
                errors.Add(Messages.DuplicateVinInCount);
            if (d.Lines.Any(l => l.CountedQuantity is not (null or 0 or 1))) errors.Add(Messages.VehicleCountQtyValues);
        }
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        if (d.WarehouseId.HasValue && !await Db.Set<Warehouse>().AnyAsync(w => w.Id == d.WarehouseId, ct))
            throw new ValidationFailedException(Messages.WarehouseNotFound);
    }

    protected override async Task OnCreatingAsync(InventoryCount e, CreateInventoryCountDto d, CancellationToken ct)
    {
        e.CountNumber = await _numbers.NextAsync("inventory_count", "IC-", ct);
        e.Status = InventoryCountStatus.Draft;
        e.CreatedByUserId = _user.UserId;
        e.CreatedByName = _user.Name;
        await PrepareAsync(e, ct);
    }

    protected override async Task OnUpdatingAsync(InventoryCount e, UpdateInventoryCountDto d, CancellationToken ct)
    {
        e.Status = InventoryCountStatus.Draft; // تعديل المرفوض يعيده مسودة للإرسال من جديد
        await PrepareAsync(e, ct);
    }

    private async Task PrepareAsync(InventoryCount e, CancellationToken ct)
    {
        if (e.CountDate == default) e.CountDate = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(e.CountedBy)) e.CountedBy = _user.Name ?? string.Empty;
        e.Location = string.IsNullOrWhiteSpace(e.Location) ? null : e.Location.Trim();
        e.WarehouseName = e.WarehouseId.HasValue
            ? await Db.Set<Warehouse>().Where(w => w.Id == e.WarehouseId).Select(w => w.NameAr).FirstOrDefaultAsync(ct)
            : null;
        await InventoryCountLines.RefreshAsync(Db, e, ct);
    }

    protected override Task OnCreatedAsync(InventoryCount e, CancellationToken ct)
        => _audit.LogAsync("CREATED", nameof(InventoryCount), e.Id.ToString(), $"جرد {e.CountNumber} ({e.Lines.Count} سطر)", ct);

    protected override Task OnUpdatedAsync(InventoryCount e, CancellationToken ct)
        => _audit.LogAsync("UPDATED", nameof(InventoryCount), e.Id.ToString(), $"معدود {e.CountedLines}/{e.TotalLines}، فروق {e.VarianceLines}", ct);

    protected override async Task OnDeletingAsync(InventoryCount e, CancellationToken ct)
    {
        if (e.Status != InventoryCountStatus.Draft || await Db.Set<InventoryCountApproval>().AnyAsync(a => a.InventoryCountId == e.Id, ct))
            throw new ConflictException(Messages.OnlyUnsubmittedDraftCountDeletable);
    }

    protected override Task OnDeletedAsync(InventoryCount e, CancellationToken ct)
        => _audit.LogAsync("DELETED", nameof(InventoryCount), e.Id.ToString(), $"حذف الجرد {e.CountNumber}", ct);

    protected override InventoryCountDto ToDto(InventoryCount entity)
    {
        var dto = Mapper.Map<InventoryCountDto>(entity);
        dto.Lines = dto.Lines.OrderBy(l => l.Sku ?? l.ChassisNumber).ThenBy(l => l.ItemName).ToList();
        return dto;
    }

    // ---------------- سير العمل ----------------
    public Task<InventoryCountApprovalDto> SubmitAsync(Guid id, CancellationToken ct = default)
        => new TransactionRunner(Db).RunAsync(async token =>
        {
            var me = _user.UserId ?? throw new UnauthorizedAppException();
            var count = await LoadAsync(id, token);
            if (count.Status is not (InventoryCountStatus.Draft or InventoryCountStatus.Rejected))
                throw new ConflictException(Messages.OnlyDraftOrRejectedCountSubmittable);
            if (count.Lines.Count == 0) throw new ValidationFailedException(Messages.NoCountLines);
            var uncounted = count.Lines.Count(l => !l.CountedQuantity.HasValue);
            if (uncounted > 0) throw new ValidationFailedException(string.Format(Messages.UncountedLinesRemain, uncounted));

            await InventoryCountLines.RefreshAsync(Db, count, token); // تجميد رصيد النظام لحظة الإرسال
            if (count.Scope == InventoryCountScope.Vehicles)
            {
                var missingIds = count.Lines.Where(l => l.Resolution == InventoryCountLineResolution.VehicleWrittenOff).Select(l => l.VehicleId).ToList();
                var reserved = await Db.Set<Vehicle>().Where(v => missingIds.Contains(v.Id) && v.Status == VehicleStatus.Reserved).Select(v => v.ChassisNumber).ToListAsync(token);
                if (reserved.Count > 0)
                    throw new ConflictException(string.Format(Messages.MissingVehiclesReserved, string.Join(Messages.ListSeparator, reserved)));
            }

            var now = DateTime.UtcNow;
            count.Status = InventoryCountStatus.PendingApproval;
            count.SubmittedAt = now;
            count.SubmittedBy = _user.Name;
            count.RejectionReason = null;

            var approval = new InventoryCountApproval
            {
                Id = Guid.NewGuid(),
                ApprovalNumber = await _numbers.NextAsync("inventory_count_approval", "ICA-", token),
                InventoryCountId = count.Id, CountNumber = count.CountNumber, Scope = count.Scope,
                Status = InventoryCountApprovalStatus.Pending,
                RequestedByUserId = me, RequestedBy = _user.Name ?? string.Empty, RequestedAt = now,
                VarianceLines = count.VarianceLines, TotalSurplusValue = count.TotalSurplusValue,
                TotalShortageValue = count.TotalShortageValue, NetVarianceValue = count.NetVarianceValue,
            };

            // سياسة اعتماد متعددة المستويات (شاشة سياسات الموافقات) تنطبق إن عُرِّفت لـ inventory_count
            var check = await _approvals.CheckAndCreateAsync(new ApprovalCheckRequestDto
            {
                DocumentType = DocumentType, ActionType = ApprovalAction, DocumentId = approval.Id,
                DocumentNumber = count.CountNumber, DocumentAmount = count.TotalSurplusValue + count.TotalShortageValue,
            }, token);
            if (check.ApprovalRequired && check.Request != null)
            {
                approval.ApprovalRequestId = check.Request.Id;
                approval.CurrentLevel = check.Request.CurrentLevel;
                approval.TotalLevels = check.Request.TotalLevels;
            }

            Db.Add(approval);
            await SaveAsync(token);
            await _audit.LogAsync("SUBMITTED", nameof(InventoryCount), count.Id.ToString(),
                $"أُرسل للاعتماد بالمستند {approval.ApprovalNumber}: فروق {count.VarianceLines} سطر، صافي {count.NetVarianceValue:0.00}", token);
            return Mapper.Map<InventoryCountApprovalDto>(approval);
        }, ct);

    public Task<InventoryCountDto> WithdrawAsync(Guid id, CancellationToken ct = default)
        => new TransactionRunner(Db).RunAsync(async token =>
        {
            var me = _user.UserId ?? throw new UnauthorizedAppException();
            var count = await LoadAsync(id, token);
            var approval = await Db.Set<InventoryCountApproval>()
                .FirstOrDefaultAsync(a => a.InventoryCountId == id && a.Status == InventoryCountApprovalStatus.Pending, token);
            if (count.Status != InventoryCountStatus.PendingApproval || approval == null) throw new ConflictException(Messages.NoPendingSubmission);
            if (approval.RequestedByUserId != me && _user.RoleId is not ("owner" or "admin"))
                throw new ForbiddenException(Messages.OnlySubmitterOrAdminCanWithdraw);

            if (approval.ApprovalRequestId.HasValue && (await _approvals.GetAsync(approval.ApprovalRequestId.Value, token)).Status == "pending")
                await _approvals.CancelAsync(approval.ApprovalRequestId.Value, token);

            approval.Status = InventoryCountApprovalStatus.Withdrawn;
            approval.Version = Guid.NewGuid();
            count.Status = InventoryCountStatus.Draft;
            await SaveAsync(token);
            await _audit.LogAsync("WITHDRAWN", nameof(InventoryCount), count.Id.ToString(), $"سحب الإرسال {approval.ApprovalNumber}", token);
            return ToDto(count);
        }, ct);

    public Task<InventoryCountDto> CancelAsync(Guid id, CancellationToken ct = default)
        => new TransactionRunner(Db).RunAsync(async token =>
        {
            var count = await LoadAsync(id, token);
            if (count.Status is not (InventoryCountStatus.Draft or InventoryCountStatus.Rejected))
                throw new ConflictException(Messages.OnlyDraftOrRejectedCountCancellable);
            count.Status = InventoryCountStatus.Cancelled;
            await SaveAsync(token);
            await _audit.LogAsync("CANCELLED", nameof(InventoryCount), count.Id.ToString(), $"إلغاء الجرد {count.CountNumber}", token);
            return ToDto(count);
        }, ct);

    public async Task<List<InventoryCountApprovalDto>> ApprovalsAsync(Guid id, CancellationToken ct = default)
    {
        if (!await Db.Set<InventoryCount>().AnyAsync(c => c.Id == id, ct)) throw new NotFoundException(string.Format(Messages.EntityNotFound, Label));
        return (await Db.Set<InventoryCountApproval>().AsNoTracking().Where(a => a.InventoryCountId == id)
                .OrderByDescending(a => a.RequestedAt).ToListAsync(ct))
            .Select(Mapper.Map<InventoryCountApprovalDto>).ToList();
    }

    private async Task<InventoryCount> LoadAsync(Guid id, CancellationToken ct)
        => await Db.Set<InventoryCount>().Include(c => c.Lines).FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException(string.Format(Messages.EntityNotFound, Label));
}
