using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using G = ERP.Core.Models.Accounting.TailoringGarmentStatuses;
using St = ERP.Core.Models.Accounting.TailoringOrderStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// الإنتاج. القواعد:
/// <list type="bullet">
/// <item>القطع لأوامر قائمة فقط. بدء أول مرحلة يتطلب بلوغ الدفعة المقدمة حدها الأدنى في الإعداد.</item>
/// <item>المرحلة فعّالة وتنطبق على نوع القطعة؛ لا تُتخطى مرحلة إلزامية قبلها. الخروج من مرحلة جودة إلى الأمام يتطلب اعتماد <c>tailoring-quality</c>.</item>
/// <item>«جاهزة»: كل المراحل الإلزامية منجزة، ولا تعديل مفتوح، ومع اشتراط الجودة تكون القطعة في مرحلة جودة ويعتمدها المخوّل.</item>
/// <item>مغادرة مرحلة لأول مرة بخياط مسند تقيّد أجر قطعته (<see cref="TailoringLabor.PostStageAsync"/>).</item>
/// <item>كل نقل سطر سجل بالمدة ومنفّذ المرحلة السابقة. البروفة تجعل القطعة «بانتظار البروفة»، ونتيجة «تعديل» تفتح طلب تعديل، وإنجاز
/// التعديل يعيدها لمرحلة الجودة. حالة الأمر تُشتق من قطعه.</item>
/// </list>
/// </summary>
public class TailoringProductionService : ITailoringProductionService
{
    private static readonly string[] Finished = { G.Delivered, G.Cancelled };

    private readonly ErpDbContext _db;
    private readonly ITransactionRunner _tx;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;
    private readonly IAuditService _audit;
    private readonly IAccountingPostingService _posting;
    private readonly ITailoringAlertService _alerts;

    public TailoringProductionService(ErpDbContext db, ITransactionRunner tx, ICurrentUser user, IPermissionService permissions, IAuditService audit,
        IAccountingPostingService posting, ITailoringAlertService alerts)
    {
        _db = db; _tx = tx; _user = user; _permissions = permissions; _audit = audit; _posting = posting; _alerts = alerts;
    }

    private string? By => _user.Name ?? _user.Email;

    // ---------- اللوحة ----------

    public async Task<TailoringBoardDto> BoardAsync(CancellationToken ct = default)
    {
        var today = SaudiTime.Today;
        var cards = await (from g in _db.Set<TailoringGarment>().AsNoTracking()
                           join o in _db.Set<TailoringOrder>().AsNoTracking() on g.OrderId equals o.Id
                           join l in _db.Set<TailoringOrderLine>().AsNoTracking() on g.LineId equals l.Id
                           where St.Open.Contains(o.Status) && !Finished.Contains(g.Status)
                           select new TailoringBoardCardDto
                           {
                               GarmentId = g.Id, Code = g.Code, Status = g.Status, StageId = g.CurrentStageId, OrderId = o.Id, OrderNumber = o.OrderNumber,
                               CustomerName = o.CustomerName, GarmentTypeName = l.GarmentTypeName, GarmentTypeId = l.GarmentTypeId, WearerName = l.WearerName,
                               EmployeeId = g.AssignedEmployeeId, DueDate = o.ExpectedDeliveryDate, Priority = o.Priority, StageStartedAt = g.StageStartedAt,
                               IsLate = o.ExpectedDeliveryDate != null && o.ExpectedDeliveryDate < today,
                               OpenModifications = _db.Set<TailoringModification>().Count(m => m.GarmentId == g.Id && m.Status == TailoringModificationStatuses.Open),
                               NextFittingAt = _db.Set<TailoringFitting>().Where(f => f.GarmentId == g.Id && f.Status == TailoringFittingStatuses.Scheduled)
                                   .Min(f => (DateTime?)f.ScheduledAt),
                           }).OrderByDescending(c => c.Priority == TailoringPriorities.Urgent).ThenBy(c => c.DueDate).ThenBy(c => c.Code).ToListAsync(ct);
        var workers = await WorkersAsync(ct);
        foreach (var c in cards) c.EmployeeName = workers.FirstOrDefault(w => w.Id == c.EmployeeId)?.Name;
        var stages = await _db.Set<TailoringStage>().AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.SortOrder).ToListAsync(ct);
        return new TailoringBoardDto
        {
            Cards = cards, Workers = workers,
            Stages = stages.Select(s => new TailoringStageDto
            {
                Id = s.Id, Code = s.Code, NameAr = s.NameAr, NameEn = s.NameEn, SortOrder = s.SortOrder, IsMandatory = s.IsMandatory,
                IsQualityCheck = s.IsQualityCheck, DefaultPieceRate = s.DefaultPieceRate, IsActive = s.IsActive, GarmentTypeIds = TypeIds(s),
            }).ToList(),
        };
    }

    private Task<List<TailoringWorkerDto>> WorkersAsync(CancellationToken ct)
        => _db.Set<Employee>().AsNoTracking().Where(e => e.Status == EmployeeStatuses.Active).OrderBy(e => e.NameAr)
            .Select(e => new TailoringWorkerDto { Id = e.Id, Code = e.Code, Name = e.NameAr }).ToListAsync(ct);

    // ---------- الإسناد والنقل ----------

    public Task AssignAsync(TailoringAssignDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            if (r.GarmentIds.Count == 0) throw new ValidationFailedException(Messages.TailoringNoGarments);
            if (r.EmployeeId is Guid eid) await RequireWorkerAsync(eid, token);
            var garments = await _db.Set<TailoringGarment>().Where(g => r.GarmentIds.Contains(g.Id)).ToListAsync(token);
            if (garments.Count != r.GarmentIds.Distinct().Count()) throw new NotFoundException(Messages.TailoringGarmentNotFound);
            foreach (var g in garments)
            {
                await RequireOpenAsync(g, token);
                g.AssignedEmployeeId = r.EmployeeId;
            }
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("TAILORING_GARMENTS_ASSIGNED", nameof(TailoringGarment), string.Join(',', garments.Select(g => g.Code)), r.EmployeeId?.ToString() ?? "-", token);
        }, ct);

    public Task MoveAsync(Guid garmentId, TailoringMoveDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var g = await LoadGarmentAsync(garmentId, token);
            var order = await RequireOpenAsync(g, token);
            if (r.EmployeeId is Guid eid) await RequireWorkerAsync(eid, token);
            var typeId = await _db.Set<TailoringOrderLine>().Where(l => l.Id == g.LineId).Select(l => l.GarmentTypeId).FirstAsync(token);
            var stages = (await _db.Set<TailoringStage>().AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.SortOrder).ToListAsync(token))
                .Where(s => Applies(s, typeId)).ToList();
            var current = stages.FirstOrDefault(s => s.Id == g.CurrentStageId);
            var visited = (await _db.Set<TailoringStageHistory>().AsNoTracking().Where(h => h.GarmentId == g.Id && h.ToStageId != null)
                .Select(h => h.ToStageId!.Value).ToListAsync(token)).ToHashSet();
            if (g.Status == G.Pending) await RequireAdvanceAsync(order, token);
            var worker = g.AssignedEmployeeId; // منفّذ المرحلة المغادَرة (قبل أي إسناد جديد)

            if (r.Ready)
            {
                if (await _db.Set<TailoringModification>().AnyAsync(m => m.GarmentId == g.Id && m.Status == TailoringModificationStatuses.Open, token))
                    throw new ConflictException(Messages.TailoringModificationOpen);
                var missing = stages.FirstOrDefault(s => s.IsMandatory && !visited.Contains(s.Id));
                if (missing != null) throw new ConflictException(string.Format(Messages.TailoringStageSkipped, missing.NameAr));
                var requireQuality = await _db.Set<TailoringSettings>().AsNoTracking().Select(s => s.RequireQualityCheck).FirstOrDefaultAsync(token);
                if (requireQuality && stages.Any(s => s.IsQualityCheck) && current?.IsQualityCheck != true)
                    throw new ConflictException(Messages.TailoringQualityRequired);
                if (current?.IsQualityCheck == true) await RequireQualityApproverAsync(token);
                Record(g, null, G.Ready, r.Note);
                g.CurrentStageId = null; g.ReadyAt = SaudiTime.Now;
            }
            else
            {
                var target = stages.FirstOrDefault(s => s.Id == r.StageId) ?? throw new ValidationFailedException(Messages.TailoringStageNotApplicable);
                if (target.Id == g.CurrentStageId) throw new ConflictException(Messages.TailoringStageSame);
                var skipped = stages.FirstOrDefault(s => s.IsMandatory && s.SortOrder < target.SortOrder && !visited.Contains(s.Id) && s.Id != g.CurrentStageId);
                if (skipped != null) throw new ConflictException(string.Format(Messages.TailoringStageSkipped, skipped.NameAr));
                if (current?.IsQualityCheck == true && target.SortOrder > current.SortOrder) await RequireQualityApproverAsync(token);
                Record(g, target.Id, target.IsQualityCheck ? G.Quality : G.InProduction, r.Note);
                g.CurrentStageId = target.Id; g.ReadyAt = null;
            }
            // أجر القطعة عن المرحلة المغادَرة (مرة واحدة لكل قطعة ومرحلة)
            await TailoringLabor.PostStageAsync(_db, _posting, g, current, worker, order.OrderNumber, By, token);
            if (r.EmployeeId != null) g.AssignedEmployeeId = r.EmployeeId;
            await _db.SaveChangesAsync(token);
            await DeriveOrderAsync(order, token);
            await _db.SaveChangesAsync(token);
        }, ct);

    public async Task<List<TailoringStageHistoryDto>> HistoryAsync(Guid garmentId, CancellationToken ct = default)
    {
        var rows = await _db.Set<TailoringStageHistory>().AsNoTracking().Where(h => h.GarmentId == garmentId).OrderBy(h => h.ChangedAt).ToListAsync(ct);
        var stages = await _db.Set<TailoringStage>().AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.NameAr, ct);
        var ids = rows.Where(h => h.EmployeeId != null).Select(h => h.EmployeeId!.Value).Distinct().ToList();
        var names = await _db.Set<Employee>().AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.NameAr, ct);
        return rows.Select(h => new TailoringStageHistoryDto
        {
            FromStage = h.FromStageId is Guid f ? stages.GetValueOrDefault(f) : null, ToStage = h.ToStageId is Guid t ? stages.GetValueOrDefault(t) : null,
            FromStatus = h.FromStatus, ToStatus = h.ToStatus, EmployeeName = h.EmployeeId is Guid e ? names.GetValueOrDefault(e) : null,
            DurationMinutes = h.DurationMinutes, Note = h.Note, ChangedBy = h.ChangedBy, ChangedAt = h.ChangedAt,
        }).ToList();
    }

    // ---------- البروفات ----------

    public async Task<List<TailoringFittingDto>> FittingsAsync(Guid? orderId, bool upcomingOnly, CancellationToken ct = default)
    {
        var q = from f in _db.Set<TailoringFitting>().AsNoTracking()
                join o in _db.Set<TailoringOrder>().AsNoTracking() on f.OrderId equals o.Id
                join g in _db.Set<TailoringGarment>().AsNoTracking() on f.GarmentId equals g.Id
                where (orderId == null || f.OrderId == orderId) && (!upcomingOnly || f.Status == TailoringFittingStatuses.Scheduled)
                orderby f.ScheduledAt
                select new TailoringFittingDto
                {
                    Id = f.Id, OrderId = o.Id, OrderNumber = o.OrderNumber, CustomerName = o.CustomerName, CustomerPhone = o.CustomerPhone,
                    GarmentId = g.Id, GarmentCode = g.Code, ScheduledAt = f.ScheduledAt, Status = f.Status, Result = f.Result, Notes = f.Notes,
                };
        return await q.Take(500).ToListAsync(ct);
    }

    public async Task<TailoringFittingDto> ScheduleFittingAsync(SaveTailoringFittingDto r, CancellationToken ct = default)
    {
        var id = await _tx.RunAsync(async token =>
        {
            var g = await LoadGarmentAsync(r.GarmentId, token);
            var order = await RequireOpenAsync(g, token);
            var f = NewFitting(g, r.ScheduledAt, r.Notes);
            Record(g, g.CurrentStageId, G.AwaitingFitting, Messages.TailoringHistoryFitting);
            await _db.SaveChangesAsync(token);
            await DeriveOrderAsync(order, token);
            await _db.SaveChangesAsync(token);
            return f.Id;
        }, ct);
        return (await FittingsAsync(null, false, ct)).First(x => x.Id == id);
    }

    public async Task<TailoringFittingDto> RecordFittingAsync(Guid fittingId, TailoringFittingResultDto r, CancellationToken ct = default)
    {
        await _tx.RunAsync(async token =>
        {
            var f = await _db.Set<TailoringFitting>().FirstOrDefaultAsync(x => x.Id == fittingId, token) ?? throw new NotFoundException(Messages.TailoringFittingNotFound);
            if (f.Status != TailoringFittingStatuses.Scheduled) throw new ConflictException(Messages.TailoringFittingClosed);
            var g = await LoadGarmentAsync(f.GarmentId, token);
            var order = await RequireOpenAsync(g, token);
            if (r.Status is not (TailoringFittingStatuses.Done or TailoringFittingStatuses.NoShow or TailoringFittingStatuses.Cancelled))
                throw new ValidationFailedException(Messages.TailoringFittingInvalid);
            f.Status = r.Status; f.Notes = Trim(r.Notes, 1000) ?? f.Notes; f.DoneAt = SaudiTime.Now;
            if (r.Status == TailoringFittingStatuses.Done)
            {
                if (r.Result is not (TailoringFittingResults.Approved or TailoringFittingResults.Modification))
                    throw new ValidationFailedException(Messages.TailoringFittingInvalid);
                f.Result = r.Result;
                if (r.Result == TailoringFittingResults.Modification)
                    await AddModificationAsync(g, f.Id, r.Instructions, r.EmployeeId, token);
            }
            if (r.NextFittingAt is DateTime next) NewFitting(g, next, null);
            if (g.Status != G.Modification)
            {
                var awaiting = r.NextFittingAt != null || await _db.Set<TailoringFitting>()
                    .AnyAsync(x => x.GarmentId == g.Id && x.Id != f.Id && x.Status == TailoringFittingStatuses.Scheduled, token);
                Record(g, g.CurrentStageId, awaiting ? G.AwaitingFitting : await StatusFromStageAsync(g, token), Messages.TailoringHistoryFittingDone);
            }
            await _db.SaveChangesAsync(token);
            await DeriveOrderAsync(order, token);
            await _db.SaveChangesAsync(token);
            return true;
        }, ct);
        return (await FittingsAsync(null, false, ct)).First(x => x.Id == fittingId);
    }

    private TailoringFitting NewFitting(TailoringGarment g, DateTime at, string? notes)
    {
        var f = new TailoringFitting { OrderId = g.OrderId, GarmentId = g.Id, ScheduledAt = at, Notes = Trim(notes, 1000), CreatedBy = By };
        _db.Add(f);
        return f;
    }

    // ---------- التعديلات ----------

    public async Task<List<TailoringModificationDto>> ModificationsAsync(Guid? orderId, bool openOnly, CancellationToken ct = default)
    {
        var rows = await (from m in _db.Set<TailoringModification>().AsNoTracking()
                          join o in _db.Set<TailoringOrder>().AsNoTracking() on m.OrderId equals o.Id
                          join g in _db.Set<TailoringGarment>().AsNoTracking() on m.GarmentId equals g.Id
                          where (orderId == null || m.OrderId == orderId) && (!openOnly || m.Status == TailoringModificationStatuses.Open)
                          orderby m.CreatedAt descending
                          select new TailoringModificationDto
                          {
                              Id = m.Id, OrderId = o.Id, OrderNumber = o.OrderNumber, GarmentId = g.Id, GarmentCode = g.Code, Instructions = m.Instructions,
                              CustomerName = o.CustomerName, CustomerPhone = o.CustomerPhone, AfterDelivery = m.AfterDelivery,
                              EmployeeId = m.AssignedEmployeeId, Status = m.Status, CreatedAt = m.CreatedAt,
                          }).Take(500).ToListAsync(ct);
        var ids = rows.Where(m => m.EmployeeId != null).Select(m => m.EmployeeId!.Value).Distinct().ToList();
        var names = await _db.Set<Employee>().AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.NameAr, ct);
        foreach (var m in rows) m.EmployeeName = m.EmployeeId is Guid e ? names.GetValueOrDefault(e) : null;
        return rows;
    }

    public async Task<TailoringModificationDto> CreateModificationAsync(SaveTailoringModificationDto r, CancellationToken ct = default)
    {
        var id = await _tx.RunAsync(async token =>
        {
            var g = await LoadGarmentAsync(r.GarmentId, token);
            if (g.Status == G.Delivered) return (await AddAfterDeliveryAsync(g, r, token)).Id;
            var order = await RequireOpenAsync(g, token);
            var m = await AddModificationAsync(g, null, r.Instructions, r.EmployeeId, token);
            await _db.SaveChangesAsync(token);
            await DeriveOrderAsync(order, token);
            await _db.SaveChangesAsync(token);
            return m.Id;
        }, ct);
        return (await ModificationsAsync(null, false, ct)).First(x => x.Id == id);
    }

    public Task<TailoringModificationDto> CompleteModificationAsync(Guid id, CancellationToken ct = default) => CloseModificationAsync(id, true, ct);
    public Task<TailoringModificationDto> CancelModificationAsync(Guid id, CancellationToken ct = default) => CloseModificationAsync(id, false, ct);

    private async Task<TailoringModificationDto> CloseModificationAsync(Guid id, bool done, CancellationToken ct)
    {
        await _tx.RunAsync(async token =>
        {
            var m = await _db.Set<TailoringModification>().FirstOrDefaultAsync(x => x.Id == id, token) ?? throw new NotFoundException(Messages.TailoringModificationNotFound);
            if (m.Status != TailoringModificationStatuses.Open) throw new ConflictException(Messages.TailoringModificationClosed);
            if (m.AfterDelivery)
            {
                // ما بعد التسليم: إنجازه = إعادة القطعة للعميل؛ لا حالة قطعة ولا أمر ولا أثر مالي
                m.Status = done ? TailoringModificationStatuses.Done : TailoringModificationStatuses.Cancelled; m.DoneAt = SaudiTime.Now;
                await _db.SaveChangesAsync(token);
                await _audit.LogAsync(done ? "TAILORING_AFTER_SALES_DONE" : "TAILORING_AFTER_SALES_CANCELLED", nameof(TailoringModification), m.Id.ToString(), m.GarmentId.ToString(), token);
                return true;
            }
            var g = await LoadGarmentAsync(m.GarmentId, token);
            var order = await RequireOpenAsync(g, token);
            m.Status = done ? TailoringModificationStatuses.Done : TailoringModificationStatuses.Cancelled; m.DoneAt = SaudiTime.Now;
            await _db.SaveChangesAsync(token);
            if (!await _db.Set<TailoringModification>().AnyAsync(x => x.GarmentId == g.Id && x.Status == TailoringModificationStatuses.Open, token))
            {
                // التعديل المنجز يعيد القطعة لأول مرحلة جودة تنطبق عليها لتُفحص من جديد
                var typeId = await _db.Set<TailoringOrderLine>().Where(l => l.Id == g.LineId).Select(l => l.GarmentTypeId).FirstAsync(token);
                var quality = done ? (await _db.Set<TailoringStage>().AsNoTracking().Where(s => s.IsActive && s.IsQualityCheck).OrderBy(s => s.SortOrder)
                    .ToListAsync(token)).FirstOrDefault(s => Applies(s, typeId)) : null;
                if (quality != null) { Record(g, quality.Id, G.Quality, Messages.TailoringHistoryModificationDone); g.CurrentStageId = quality.Id; g.ReadyAt = null; }
                else Record(g, g.CurrentStageId, await StatusFromStageAsync(g, token), done ? Messages.TailoringHistoryModificationDone : null);
            }
            await _db.SaveChangesAsync(token);
            await DeriveOrderAsync(order, token);
            await _db.SaveChangesAsync(token);
            return true;
        }, ct);
        return (await ModificationsAsync(null, false, ct)).First(x => x.Id == id);
    }

    /// <summary>
    /// تعديل ما بعد التسليم: لقطعة «مُسلَّمة» بتسليم قائم، واحد مفتوح لكل قطعة. مجاني (ت13) فلا فاتورة؛ القطعة تبقى مُسلَّمة والأمر لا يُعاد
    /// اشتقاقه، والأجر إن لزم يُقيَّد يدوياً من شاشة الأجور.
    /// </summary>
    private async Task<TailoringModification> AddAfterDeliveryAsync(TailoringGarment g, SaveTailoringModificationDto r, CancellationToken ct)
    {
        var text = Trim(r.Instructions, 2000) ?? throw new ValidationFailedException(Messages.TailoringModificationInstructionsRequired);
        if (r.EmployeeId is Guid eid) await RequireWorkerAsync(eid, ct);
        var status = await _db.Set<TailoringOrder>().AsNoTracking().Where(o => o.Id == g.OrderId).Select(o => o.Status).FirstAsync(ct);
        if (status == St.Cancelled) throw new ConflictException(Messages.TailoringGarmentFinished);
        if (await _db.Set<TailoringModification>().AnyAsync(m => m.GarmentId == g.Id && m.Status == TailoringModificationStatuses.Open, ct))
            throw new ConflictException(Messages.TailoringModificationOpen);
        var m = new TailoringModification
        {
            OrderId = g.OrderId, GarmentId = g.Id, AfterDelivery = true, Instructions = text, AssignedEmployeeId = r.EmployeeId, CreatedBy = By,
        };
        _db.Add(m);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("TAILORING_AFTER_SALES_OPENED", nameof(TailoringModification), m.Id.ToString(), g.Code, ct);
        return m;
    }

    private async Task<TailoringModification> AddModificationAsync(TailoringGarment g, Guid? fittingId, string? instructions, Guid? employeeId, CancellationToken ct)
    {
        var text = Trim(instructions, 2000) ?? throw new ValidationFailedException(Messages.TailoringModificationInstructionsRequired);
        if (employeeId is Guid eid) await RequireWorkerAsync(eid, ct);
        var m = new TailoringModification
        {
            OrderId = g.OrderId, GarmentId = g.Id, FittingId = fittingId, Instructions = text, AssignedEmployeeId = employeeId, CreatedBy = By,
        };
        _db.Add(m);
        Record(g, g.CurrentStageId, G.Modification, text.Length > 200 ? text[..200] : text);
        g.ReadyAt = null;
        if (employeeId != null) g.AssignedEmployeeId = employeeId;
        return m;
    }

    // ---------- مساعدات ----------

    private void Record(TailoringGarment g, Guid? toStage, string toStatus, string? note)
        => TailoringFlow.Record(_db, g, toStage, toStatus, note, By, _user.UserId);

    private async Task<string> StatusFromStageAsync(TailoringGarment g, CancellationToken ct)
    {
        if (g.CurrentStageId is not Guid sid) return g.ReadyAt != null ? G.Ready : G.Pending;
        var quality = await _db.Set<TailoringStage>().AsNoTracking().Where(s => s.Id == sid).Select(s => s.IsQualityCheck).FirstOrDefaultAsync(ct);
        return quality ? G.Quality : G.InProduction;
    }

    /// <summary>حالة الأمر من قطعه (<see cref="TailoringFlow.DeriveOrderAsync"/>)؛ إن صار كله جاهزاً للتسليم يُشعَر من يسلّمه.</summary>
    private async Task DeriveOrderAsync(TailoringOrder o, CancellationToken ct)
    {
        var before = o.Status;
        await TailoringFlow.DeriveOrderAsync(_db, o, By, _user.UserId, ct);
        if (o.Status == St.ReadyForDelivery && before != St.ReadyForDelivery)
        {
            await _db.SaveChangesAsync(ct);
            await _alerts.OrderReadyAsync(o.Id, ct);
        }
    }

    private async Task<TailoringGarment> LoadGarmentAsync(Guid id, CancellationToken ct)
        => await _db.Set<TailoringGarment>().FirstOrDefaultAsync(g => g.Id == id, ct) ?? throw new NotFoundException(Messages.TailoringGarmentNotFound);

    private async Task<TailoringOrder> RequireOpenAsync(TailoringGarment g, CancellationToken ct)
    {
        if (Finished.Contains(g.Status)) throw new ConflictException(Messages.TailoringGarmentFinished);
        var o = await _db.Set<TailoringOrder>().FirstAsync(x => x.Id == g.OrderId, ct);
        if (!St.Open.Contains(o.Status)) throw new ConflictException(Messages.TailoringOrderNotConfirmed);
        return o;
    }

    private async Task RequireAdvanceAsync(TailoringOrder o, CancellationToken ct)
    {
        var percent = await _db.Set<TailoringSettings>().AsNoTracking().Select(s => s.MinAdvancePercent).FirstOrDefaultAsync(ct);
        var min = DocumentPricing.Round(o.GrandTotal * percent / 100m);
        if (min > 0 && o.AdvanceAmount < min) throw new ConflictException(string.Format(Messages.TailoringAdvanceRequiredForProduction, min));
    }

    private async Task RequireQualityApproverAsync(CancellationToken ct)
    {
        // الرفض برسالة «تواصل مع مسؤول الاعتماد» والأدوار المخوّلة
        await _permissions.DemandAsync(PermissionCatalog.TailoringQualityScreenId, ScreenAction.Approve, null, ct);
    }

    private async Task RequireWorkerAsync(Guid id, CancellationToken ct)
    {
        if (!await _db.Set<Employee>().AnyAsync(e => e.Id == id && e.Status == EmployeeStatuses.Active, ct))
            throw new ValidationFailedException(Messages.TailoringEmployeeNotFound);
    }

    private static List<Guid> TypeIds(TailoringStage s)
        => string.IsNullOrWhiteSpace(s.GarmentTypeIds) ? new() : s.GarmentTypeIds.Split(',').Select(Guid.Parse).ToList();
    private static bool Applies(TailoringStage s, Guid typeId) { var ids = TypeIds(s); return ids.Count == 0 || ids.Contains(typeId); }

    private static string? Trim(string? v, int max)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        v = v.Trim();
        return v.Length > max ? v[..max] : v;
    }
}
