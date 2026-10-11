using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Accounting;

namespace ERP.Service.Services.Shared;

/// <summary>
/// اعتماد الجرد وتسويته. القرار يمرّ بسياسة الاعتماد متعددة المستويات إن ارتبط بها المستند (IApprovalService)،
/// وإلا بقاعدة فصل المهام: لا يعتمد المُرسِل جرده إلا المالك. الاعتماد النهائي يسجّل حركات تسوية المخزون
/// (IInventoryService) أو يشطب/يستعيد المركبات، ويرحّل قيد الفروق على حساب فروقات الجرد — كل ذلك في معاملة واحدة.
/// </summary>
public class InventoryCountApprovalService : IInventoryCountApprovalService
{
    private const string SourceType = "inventory_count";

    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;
    private readonly ITransactionRunner _tx;
    private readonly IApprovalService _approvals;
    private readonly IInventoryService _inventory;
    private readonly IAccountingPostingService _posting;
    private readonly IAuditService _audit;
    private readonly INotificationService _notifications;

    public InventoryCountApprovalService(ErpDbContext db, ICurrentUser user, ITransactionRunner tx, IApprovalService approvals,
        IInventoryService inventory, IAccountingPostingService posting, IAuditService audit, INotificationService notifications)
    {
        _db = db; _user = user; _tx = tx; _approvals = approvals; _inventory = inventory; _posting = posting; _audit = audit; _notifications = notifications;
    }

    public async Task<PagedResult<InventoryCountApprovalDto>> ListAsync(InventoryCountScope? scope, PaginationParams p, CancellationToken ct = default)
    {
        var q = _db.Set<InventoryCountApproval>().AsNoTracking();
        if (scope.HasValue) q = q.Where(a => a.Scope == scope);
        if (!string.IsNullOrWhiteSpace(p.Status) && Enum.TryParse<InventoryCountApprovalStatus>(p.Status.Replace("_", ""), true, out var status))
            q = q.Where(a => a.Status == status);
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var t = p.SearchTerm.Trim();
            q = q.Where(a => a.ApprovalNumber.Contains(t) || a.CountNumber.Contains(t) || a.RequestedBy.Contains(t));
        }
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(a => a.RequestedAt)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);
        return new PagedResult<InventoryCountApprovalDto>
        {
            Items = items.Select(Mapper.Map<InventoryCountApprovalDto>).ToList(), TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    public async Task<InventoryCountApprovalDto> GetAsync(Guid id, CancellationToken ct = default)
        => Mapper.Map<InventoryCountApprovalDto>(await _db.Set<InventoryCountApproval>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException(Messages.InventoryCountApprovalNotFound));

    /// <summary>
    /// المعاينة تنفّذ التسوية والترحيل الحقيقيين داخل معاملة تُلغى دائماً (كمعاينة قيد الفاتورة)، فتطابق القيد الفعلي
    /// حرفياً بما فيها تكلفة الصرف وفق سياسة التكلفة، دون حفظ حركات أو استهلاك رقم قيد.
    /// </summary>
    public async Task<InventoryCountJournalDto> JournalAsync(Guid id, CancellationToken ct = default)
    {
        var approval = await _db.Set<InventoryCountApproval>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException(Messages.InventoryCountApprovalNotFound);
        if (approval.JournalEntryId.HasValue) return await JournalOfAsync(approval.JournalEntryId.Value, ct);
        if (approval.Status != InventoryCountApprovalStatus.Pending) return new InventoryCountJournalDto();

        if (_db.Database.CurrentTransaction != null) throw new InvalidOperationException(Messages.JournalPreviewInsideTransaction);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var count = await _db.Set<InventoryCount>().Include(c => c.Lines).FirstAsync(c => c.Id == approval.InventoryCountId, ct);
            var journal = count.Scope == InventoryCountScope.Items ? await AdjustItemsAsync(count, ct) : await AdjustVehiclesAsync(count, ct);
            if (journal == null) return new InventoryCountJournalDto { IsPreview = true };
            var preview = await JournalOfAsync(journal.JournalEntryId, ct);
            preview.IsPreview = true;
            preview.JournalEntryId = null;
            preview.EntryNumber = null;
            return preview;
        }
        finally
        {
            await tx.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
        }
    }

    private async Task<InventoryCountJournalDto> JournalOfAsync(Guid journalEntryId, CancellationToken ct)
    {
        var entry = await _db.Set<JournalEntry>().AsNoTracking().Include(e => e.Lines).FirstAsync(e => e.Id == journalEntryId, ct);
        return new InventoryCountJournalDto
        {
            JournalEntryId = entry.Id, EntryNumber = entry.EntryNumber, Date = entry.Date, Description = entry.Description,
            Lines = entry.Lines.OrderByDescending(l => l.Debit > 0).ThenBy(l => l.AccountCode)
                .Select(l => new InvoiceJournalLineDto { AccountCode = l.AccountCode, AccountName = l.AccountName, Debit = l.Debit, Credit = l.Credit, Notes = l.Notes })
                .ToList(),
            TotalDebit = entry.TotalDebit, TotalCredit = entry.TotalCredit,
        };
    }

    public Task<InventoryCountApprovalDto> ApproveAsync(Guid id, ApprovalDecisionDto d, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var (approval, count) = await LoadPendingAsync(id, token);

            if (approval.ApprovalRequestId.HasValue)
            {
                var request = await _approvals.GetAsync(approval.ApprovalRequestId.Value, token);
                if (request.Status == "pending") request = await _approvals.ApproveAsync(request.Id, d, token); // يفرض معتمد المستوى الحالي
                else if (request.Status == "approved") EnsureNotRequester(approval);
                else throw new ConflictException(Messages.LinkedApprovalRequestClosed);

                approval.CurrentLevel = request.CurrentLevel;
                if (request.Status != "approved")
                {
                    approval.Version = Guid.NewGuid();
                    await _db.SaveChangesAsync(token);
                    await _audit.LogAsync("LEVEL_APPROVED", nameof(InventoryCountApproval), approval.Id.ToString(),
                        $"اعتماد مستوى — التالي {request.CurrentLevel}/{request.TotalLevels}" + Comment(d), token);
                    return Mapper.Map<InventoryCountApprovalDto>(approval);
                }
            }
            else
            {
                EnsureNotRequester(approval);
            }

            var journal = count.Scope == InventoryCountScope.Items ? await AdjustItemsAsync(count, token) : await AdjustVehiclesAsync(count, token);
            InventoryCountLines.Summarize(count); // القيم الفعلية بتكلفة حركات التسوية

            var now = DateTime.UtcNow;
            approval.Status = InventoryCountApprovalStatus.Approved;
            approval.DecidedByUserId = _user.UserId; approval.DecidedBy = _user.Name; approval.DecidedAt = now; approval.DecisionComment = d.Comment;
            approval.JournalEntryId = journal?.JournalEntryId; approval.JournalEntryNumber = journal?.EntryNumber;
            approval.Version = Guid.NewGuid();
            count.Status = InventoryCountStatus.Approved;
            count.ApprovedAt = now; count.ApprovedBy = _user.Name;
            count.JournalEntryId = journal?.JournalEntryId; count.JournalEntryNumber = journal?.EntryNumber;
            await _db.SaveChangesAsync(token);

            await _audit.LogAsync("APPROVED", nameof(InventoryCountApproval), approval.Id.ToString(), $"اعتماد الجرد {count.CountNumber}" + Comment(d), token);
            await _audit.LogAsync("ADJUSTED", nameof(InventoryCount), count.Id.ToString(),
                $"تسوية {count.VarianceLines} سطر: زيادة {count.TotalSurplusValue:0.00}، عجز {count.TotalShortageValue:0.00}" + (journal != null ? $"، القيد {journal.EntryNumber}" : ""), token);
            if (approval.ApprovalRequestId == null) await NotifyRequesterAsync(approval, "approval_approved", "تم اعتماد الجرد وتسوية الفروق", token);
            return Mapper.Map<InventoryCountApprovalDto>(approval);
        }, ct);

    public Task<InventoryCountApprovalDto> RejectAsync(Guid id, ApprovalDecisionDto d, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            if (string.IsNullOrWhiteSpace(d.Comment)) throw new ValidationFailedException(Messages.RejectionReasonRequired);
            var (approval, count) = await LoadPendingAsync(id, token);

            if (approval.ApprovalRequestId.HasValue && (await _approvals.GetAsync(approval.ApprovalRequestId.Value, token)).Status == "pending")
                await _approvals.RejectAsync(approval.ApprovalRequestId.Value, d, token); // يفرض معتمد المستوى الحالي
            else
                EnsureNotRequester(approval);

            approval.Status = InventoryCountApprovalStatus.Rejected;
            approval.DecidedByUserId = _user.UserId; approval.DecidedBy = _user.Name; approval.DecidedAt = DateTime.UtcNow; approval.DecisionComment = d.Comment;
            approval.Version = Guid.NewGuid();
            count.Status = InventoryCountStatus.Rejected;
            count.RejectionReason = d.Comment;
            await _db.SaveChangesAsync(token);

            await _audit.LogAsync("REJECTED", nameof(InventoryCountApproval), approval.Id.ToString(), $"رفض الجرد {count.CountNumber}" + Comment(d), token);
            if (approval.ApprovalRequestId == null) await NotifyRequesterAsync(approval, "approval_rejected", "تم رفض الجرد", token);
            return Mapper.Map<InventoryCountApprovalDto>(approval);
        }, ct);

    // ---------------- التسوية ----------------
    /// <summary>حركة تسوية لكل فرق (الزيادة بمتوسط التكلفة، والعجز بتكلفة الصرف وفق السياسة) ثم قيد الفروق.</summary>
    private async Task<PostingResult?> AdjustItemsAsync(InventoryCount count, CancellationToken ct)
    {
        foreach (var line in count.Lines.Where(l => l.VarianceQuantity != 0))
        {
            var isIn = line.VarianceQuantity > 0;
            var unitCost = isIn ? await _db.Set<Product>().Where(p => p.Id == line.ItemId).Select(p => p.AverageCost).FirstAsync(ct) : 0;
            var movement = await _inventory.RecordMovementAsync(new RecordStockMovementDto
            {
                ItemId = line.ItemId!.Value, WarehouseId = count.WarehouseId,
                Type = isIn ? StockMovementType.AdjustmentIn : StockMovementType.AdjustmentOut,
                Quantity = Math.Abs(line.VarianceQuantity), UnitCost = unitCost, ReferenceNumber = count.CountNumber, Date = DateTime.UtcNow,
                Notes = $"تسوية جرد {count.CountNumber}", SourceType = SourceType, SourceId = count.Id,
            }, ct);
            line.UnitCost = movement.UnitCost;
            line.VarianceValue = Math.Round(line.VarianceQuantity * line.UnitCost, 2);
        }
        return await PostVarianceAsync(count, DefaultAccounts.Inventory, ct);
    }

    /// <summary>المفقودة تُشطب، والمعثور عليها بعد شطبها تُستعاد، والموجودة في موقع آخر يُحدَّث موقعها.</summary>
    private async Task<PostingResult?> AdjustVehiclesAsync(InventoryCount count, CancellationToken ct)
    {
        var ids = count.Lines.Where(l => l.VehicleId.HasValue && l.VarianceQuantity != 0).Select(l => l.VehicleId!.Value).ToList();
        var vehicles = await _db.Set<Vehicle>().Where(v => ids.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
        foreach (var line in count.Lines.Where(l => l.VehicleId.HasValue && l.VarianceQuantity != 0))
        {
            var v = vehicles[line.VehicleId!.Value];
            switch (line.Resolution)
            {
                case InventoryCountLineResolution.VehicleWrittenOff:
                    if (v.Status != VehicleStatus.Available)
                        throw new ConflictException(string.Format(Messages.CountVehicleNoLongerAvailable, v.ChassisNumber));
                    v.Status = VehicleStatus.WrittenOff;
                    line.UnitCost = v.TotalCost; line.VarianceValue = -v.TotalCost;
                    break;
                case InventoryCountLineResolution.VehicleReinstated:
                    if (v.Status != VehicleStatus.WrittenOff) throw new ConflictException(string.Format(Messages.CountVehicleStatusChanged, v.ChassisNumber));
                    v.Status = VehicleStatus.Available;
                    if (count.Location != null) v.Location = count.Location;
                    line.UnitCost = v.TotalCost; line.VarianceValue = v.TotalCost;
                    break;
                case InventoryCountLineResolution.VehicleRelocated:
                    if (count.Location != null) v.Location = count.Location;
                    break;
            }
        }
        await _db.SaveChangesAsync(ct);
        return await PostVarianceAsync(count, DefaultAccounts.VehicleInventory, ct);
    }

    /// <summary>زيادة الجرد: مدين المخزون / دائن فروقات الجرد، والعجز بالعكس. لا قيد إن لم يكن للفروق قيمة.</summary>
    private async Task<PostingResult?> PostVarianceAsync(InventoryCount count, string inventoryAccount, CancellationToken ct)
    {
        var surplus = count.Lines.Where(l => l.VarianceValue > 0).Sum(l => l.VarianceValue);
        var shortage = -count.Lines.Where(l => l.VarianceValue < 0).Sum(l => l.VarianceValue);
        var lines = new List<PostingLine>();
        if (surplus > 0) lines.AddRange(new PostingLine[] { new(inventoryAccount, surplus, 0, "زيادة جرد"), new(DefaultAccounts.InventoryAdjustment, 0, surplus, "زيادة جرد") });
        if (shortage > 0) lines.AddRange(new PostingLine[] { new(DefaultAccounts.InventoryAdjustment, shortage, 0, "عجز جرد"), new(inventoryAccount, 0, shortage, "عجز جرد") });
        if (lines.Count == 0) return null;

        await DefaultAccounts.EnsureAsync(_db, ct, inventoryAccount, DefaultAccounts.InventoryAdjustment);
        return await _posting.PostAsync(new GenericPostingRequest
        {
            Date = DateTime.UtcNow, Description = $"تسوية فروق الجرد {count.CountNumber}",
            SourceType = SourceType, SourceId = count.Id, SourceNumber = count.CountNumber, Lines = lines,
        }, ct);
    }

    // ---------------- مساعدات ----------------
    private async Task<(InventoryCountApproval Approval, InventoryCount Count)> LoadPendingAsync(Guid id, CancellationToken ct)
    {
        _ = _user.UserId ?? throw new UnauthorizedAppException();
        var approval = await _db.Set<InventoryCountApproval>().FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException(Messages.InventoryCountApprovalNotFound);
        if (approval.Status != InventoryCountApprovalStatus.Pending) throw new ConflictException(Messages.ApprovalAlreadyDecided);
        var count = await _db.Set<InventoryCount>().Include(c => c.Lines).FirstAsync(c => c.Id == approval.InventoryCountId, ct);
        if (count.Status != InventoryCountStatus.PendingApproval) throw new ConflictException(Messages.CountNotPendingApproval);
        return (approval, count);
    }

    /// <summary>فصل المهام: مُرسِل الجرد لا يبتّ فيه إلا إن كان المالك (كقاعدة سياسات الموافقات).</summary>
    private void EnsureNotRequester(InventoryCountApproval approval)
    {
        if (approval.RequestedByUserId == _user.UserId && _user.RoleId != "owner")
            throw new ForbiddenException(Messages.CannotDecideOwnCount);
    }

    private static string Comment(ApprovalDecisionDto d) => string.IsNullOrWhiteSpace(d.Comment) ? "" : $" — {d.Comment}";

    private Task NotifyRequesterAsync(InventoryCountApproval a, string type, string title, CancellationToken ct)
        => _notifications.NotifyAsync(new NotifyRequestDto
        {
            RecipientUserId = a.RequestedByUserId, Type = type, Title = title,
            Body = $"{a.ApprovalNumber}: الجرد {a.CountNumber}" + (a.DecisionComment != null ? $" — {a.DecisionComment}" : ""),
            RelatedDocType = SourceType, RelatedDocId = a.InventoryCountId, RelatedDocNumber = a.CountNumber,
        }, ct);
}
