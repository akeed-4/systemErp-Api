using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using St = ERP.Core.Models.Accounting.WorkOrderStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// استقبال المركبات وأوامر العمل (W1). القواعد:
/// <list type="bullet">
/// <item>الاستقبال لعميل موجود بمركبة أو أكثر (حتى <see cref="MaxVehicles"/>)، لكل مركبة أمر عمل مستقل برقمه. المركبة من سجل العميل نفسه
/// (أو تُسجَّل له في الاستقبال)، نشطة، وبلا أمر قائم؛ يُقفل صفها حتى لا يُفتح لها أمران متزامنان. اللحظة محلية لا مستقبلية.</item>
/// <item>الأمر يحفظ لقطة العميل واللوحة (O15). عدّاد الاستقبال لا يقل عن آخر قراءة للمركبة ويرفعها؛ الوقود 0-100؛ الموعد لا يسبق الاستقبال.</item>
/// <item>الانتقالات اليدوية من <see cref="Transitions"/> فقط؛ الرجوع لحالة سابقة بسبب، والتوقف بسبب توقف نشط، و«معتمد» بصلاحية الاعتماد،
/// والفحص قبل الموافقة إن اشترطه الإعداد. المرحلة الفرعية نشطة ومن الحالة الهدف. الفوترة والتسليم والإغلاق من مراحلها (W4).</item>
/// <item>التعديل والإلغاء بسبب قبل الفوترة؛ الحذف لأمر «مستلم» لم يتحرك (ويُحذف استقباله إن خلا).</item>
/// </list>
/// </summary>
public class WorkOrderService : IWorkOrderService
{
    public const int MaxVehicles = 20;
    private const int MaxDamages = 60;
    private const string Screen = PermissionCatalog.WorkshopOrdersScreenId;
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(15);

    /// <summary>الانتقالات اليدوية المسموحة: الأمام ثم الرجوع (الرجوع يتطلب سبباً).</summary>
    internal static readonly Dictionary<string, (string[] Forward, string[] Back)> Transitions = new()
    {
        [St.Received] = (new[] { St.Inspection, St.AwaitingApproval }, Array.Empty<string>()),
        [St.Inspection] = (new[] { St.AwaitingApproval }, new[] { St.Received }),
        [St.AwaitingApproval] = (new[] { St.Approved }, new[] { St.Inspection }),
        [St.Approved] = (new[] { St.InProgress }, new[] { St.AwaitingApproval }),
        [St.InProgress] = (new[] { St.Paused, St.Quality }, new[] { St.AwaitingApproval }),
        [St.Paused] = (new[] { St.InProgress }, Array.Empty<string>()),
        [St.Quality] = (new[] { St.Ready }, new[] { St.InProgress }),
        [St.Ready] = (Array.Empty<string>(), new[] { St.InProgress }),
    };

    private readonly ErpDbContext _db;
    private readonly IServiceVehicleService _vehicles;
    private readonly INumberSequenceService _numbers;
    private readonly ITransactionRunner _tx;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;
    private readonly IAuditService _audit;

    public WorkOrderService(ErpDbContext db, IServiceVehicleService vehicles, INumberSequenceService numbers, ITransactionRunner tx, ICurrentUser user,
        IPermissionService permissions, IAuditService audit)
    {
        _db = db; _vehicles = vehicles; _numbers = numbers; _tx = tx; _user = user; _permissions = permissions; _audit = audit;
    }

    // ---------- الاستقبال ----------

    public async Task<List<WorkshopReceptionDto>> ReceptionsAsync(Guid? customerId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var q = _db.Set<WorkshopReception>().AsNoTracking();
        if (customerId is Guid cid) q = q.Where(r => r.CustomerId == cid);
        if (from is DateTime f) q = q.Where(r => r.ReceivedAt >= f.Date);
        if (to is DateTime t) q = q.Where(r => r.ReceivedAt < t.Date.AddDays(1));
        return await ReceptionDtosAsync(await q.OrderByDescending(r => r.ReceivedAt).Take(500).ToListAsync(ct), ct);
    }

    public async Task<WorkshopReceptionDto> GetReceptionAsync(Guid id, CancellationToken ct = default)
    {
        var r = await _db.Set<WorkshopReception>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(Messages.WorkshopReceptionNotFound);
        return (await ReceptionDtosAsync(new List<WorkshopReception> { r }, ct)).Single();
    }

    public Task<WorkshopReceptionDto> CreateReceptionAsync(SaveWorkshopReceptionDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var customer = await _db.Set<Customer>().AsNoTracking().Where(c => c.Id == request.CustomerId).Select(c => new { c.Id, c.NameAr, c.Phone })
                .FirstOrDefaultAsync(token) ?? throw new ValidationFailedException(Messages.WorkshopCustomerNotFound);
            if (request.Vehicles.Count == 0) throw new ValidationFailedException(Messages.WorkshopReceptionVehiclesRequired);
            if (request.Vehicles.Count > MaxVehicles) throw new ValidationFailedException(string.Format(Messages.WorkshopReceptionTooManyVehicles, MaxVehicles));
            var warehouseId = request.WarehouseId ?? (await _db.Set<WorkshopSettings>().AsNoTracking().FirstOrDefaultAsync(token))?.DefaultWarehouseId;
            if (warehouseId is Guid wh && !await _db.Set<Warehouse>().AnyAsync(w => w.Id == wh, token))
                throw new ValidationFailedException(Messages.WorkshopWarehouseNotFound);
            var now = SaudiTime.Now;
            var at = request.ReceivedAt is DateTime t ? Local(t) : now;
            if (at > now + FutureTolerance) throw new ValidationFailedException(Messages.WorkshopReceptionInFuture);

            var reception = new WorkshopReception
            {
                CustomerId = customer.Id, WarehouseId = warehouseId, ReceivedAt = at, ContactName = Trim(request.ContactName, 200),
                ContactPhone = Trim(request.ContactPhone, 30), Notes = Trim(request.Notes, 1000), CreatedBy = _user.Name ?? _user.Email, CreatedByUserId = _user.UserId,
            };
            reception.ReceptionNumber = await _numbers.AssignAsync(new DocumentNumberRequest
            {
                Key = "workshop_reception", DefaultPrefix = "WRC-", WarehouseId = warehouseId, Date = at.Date,
                Exists = (n, tk) => _db.Set<WorkshopReception>().AnyAsync(x => x.ReceptionNumber == n, tk),
            }, token);
            _db.Add(reception);
            await _db.SaveChangesAsync(token);

            var seen = new HashSet<Guid>();
            foreach (var v in request.Vehicles)
                await OpenOrderAsync(reception, customer.NameAr, customer.Phone, v, seen, token);
            await _audit.LogAsync("WORKSHOP_RECEPTION_CREATED", nameof(WorkshopReception), reception.Id.ToString(),
                $"{reception.ReceptionNumber} • {request.Vehicles.Count}", token);
            return await GetReceptionAsync(reception.Id, token);
        }, ct);

    public async Task<WorkshopReceptionDto> UpdateReceptionAsync(Guid id, UpdateWorkshopReceptionDto request, CancellationToken ct = default)
    {
        var r = await _db.Set<WorkshopReception>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException(Messages.WorkshopReceptionNotFound);
        r.ContactName = Trim(request.ContactName, 200); r.ContactPhone = Trim(request.ContactPhone, 30); r.Notes = Trim(request.Notes, 1000);
        await _db.SaveChangesAsync(ct);
        return await GetReceptionAsync(id, ct);
    }

    public Task<WorkOrderDto> AddVehicleAsync(Guid receptionId, ReceptionVehicleDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var reception = await _db.Set<WorkshopReception>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == receptionId, token)
                ?? throw new NotFoundException(Messages.WorkshopReceptionNotFound);
            var customer = await _db.Set<Customer>().AsNoTracking().Where(c => c.Id == reception.CustomerId).Select(c => new { c.NameAr, c.Phone }).FirstAsync(token);
            if (await _db.Set<WorkOrder>().CountAsync(o => o.ReceptionId == receptionId, token) >= MaxVehicles)
                throw new ValidationFailedException(string.Format(Messages.WorkshopReceptionTooManyVehicles, MaxVehicles));
            var order = await OpenOrderAsync(reception, customer.NameAr, customer.Phone, request, new HashSet<Guid>(), token);
            return await GetAsync(order.Id, token);
        }, ct);

    private async Task<WorkOrder> OpenOrderAsync(WorkshopReception reception, string customerName, string? customerPhone, ReceptionVehicleDto r,
        HashSet<Guid> seen, CancellationToken ct)
    {
        Guid vehicleId;
        if (r.VehicleId is Guid existing) vehicleId = existing;
        else if (r.NewVehicle != null)
        {
            r.NewVehicle.CustomerId = reception.CustomerId;
            vehicleId = (await _vehicles.CreateAsync(r.NewVehicle, ct)).Id;
        }
        else throw new ValidationFailedException(Messages.WorkshopReceptionVehicleRequired);
        if (!seen.Add(vehicleId)) throw new ValidationFailedException(Messages.WorkshopReceptionDuplicateVehicle);

        // قفل صف المركبة: أمر قائم واحد لها
        await _db.Set<ServiceVehicle>().Where(v => v.Id == vehicleId).ExecuteUpdateAsync(s => s.SetProperty(v => v.LastOdometer, v => v.LastOdometer), ct);
        var vehicle = await _db.Set<ServiceVehicle>().FirstOrDefaultAsync(v => v.Id == vehicleId, ct) ?? throw new ValidationFailedException(Messages.ServiceVehicleNotFound);
        await _db.Entry(vehicle).ReloadAsync(ct);
        if (vehicle.CustomerId != reception.CustomerId) throw new ValidationFailedException(Messages.WorkshopVehicleOtherCustomer);
        if (!vehicle.IsActive) throw new ValidationFailedException(Messages.ServiceVehicleInactive);
        var open = await _db.Set<WorkOrder>().Where(o => o.VehicleId == vehicleId && St.Open.Contains(o.Status)).Select(o => o.OrderNumber).FirstOrDefaultAsync(ct);
        if (open != null) throw new ConflictException(string.Format(Messages.WorkshopVehicleHasOpenOrder, open));

        var o = new WorkOrder
        {
            ReceptionId = reception.Id, WarehouseId = reception.WarehouseId, CustomerId = reception.CustomerId, CustomerName = customerName,
            CustomerPhone = Trim(customerPhone, 30), VehicleId = vehicle.Id, PlateSnapshot = vehicle.PlateNumber, Status = St.Received,
            ReceivedAt = reception.ReceivedAt, CreatedBy = _user.Name ?? _user.Email, CreatedByUserId = _user.UserId,
        };
        await ApplyDetailsAsync(o, vehicle, r.OdometerIn, r.FuelLevelIn, r.Complaint, r.Accessories, r.Priority, r.PromisedAt, r.ServiceAdvisorUserId, r.ServiceAdvisorName, r.Notes, ct);
        if (r.Damages.Count > MaxDamages) throw new ValidationFailedException(string.Format(Messages.WorkOrderTooManyDamages, MaxDamages));
        var order = 0;
        foreach (var d in r.Damages) o.DamageMarks.Add(NewDamage(d, ++order));
        o.OrderNumber = await _numbers.AssignAsync(new DocumentNumberRequest
        {
            Key = "work_order", DefaultPrefix = "WO-", WarehouseId = o.WarehouseId, Date = o.ReceivedAt.Date,
            Exists = (n, tk) => _db.Set<WorkOrder>().AnyAsync(x => x.OrderNumber == n, tk),
        }, ct);
        _db.Add(o);
        await _db.SaveChangesAsync(ct);
        AddHistory(o, null, St.Received, null, null, null);
        await _db.SaveChangesAsync(ct);
        await LogAsync("WORK_ORDER_CREATED", o, ServiceVehicleService.Label(vehicle), ct);
        return o;
    }

    // ---------- أمر العمل ----------

    public async Task<List<WorkOrderDto>> ListAsync(string? status, Guid? customerId, Guid? vehicleId, Guid? receptionId, CancellationToken ct = default)
    {
        var q = _db.Set<WorkOrder>().AsNoTracking().Include(o => o.DamageMarks).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var s = status.Trim().ToLowerInvariant();
            q = s == "open" ? q.Where(o => St.Open.Contains(o.Status)) : q.Where(o => o.Status == s);
        }
        if (customerId is Guid cid) q = q.Where(o => o.CustomerId == cid);
        if (vehicleId is Guid vid) q = q.Where(o => o.VehicleId == vid);
        if (receptionId is Guid rid) q = q.Where(o => o.ReceptionId == rid);
        return await DtosAsync(await q.OrderByDescending(o => o.ReceivedAt).ThenBy(o => o.OrderNumber).Take(1000).ToListAsync(ct), withHistory: false, ct);
    }

    public async Task<WorkOrderDto> GetAsync(Guid id, CancellationToken ct = default)
        => (await DtosAsync(new List<WorkOrder> { await FindAsync(id, track: false, ct) }, withHistory: true, ct)).Single();

    public async Task<WorkOrderDto> UpdateAsync(Guid id, UpdateWorkOrderDto r, CancellationToken ct = default)
    {
        var o = await FindAsync(id, track: true, ct);
        if (!St.Editable.Contains(o.Status)) throw new ConflictException(Messages.WorkOrderNotEditable);
        var vehicle = await _db.Set<ServiceVehicle>().FirstAsync(v => v.Id == o.VehicleId, ct);
        await ApplyDetailsAsync(o, vehicle, r.OdometerIn, r.FuelLevelIn, r.Complaint, r.Accessories, r.Priority, r.PromisedAt, r.ServiceAdvisorUserId, r.ServiceAdvisorName, r.Notes, ct);
        await _db.SaveChangesAsync(ct);
        await LogAsync("WORK_ORDER_UPDATED", o, null, ct);
        return await GetAsync(id, ct);
    }

    public Task<WorkOrderDto> ChangeStatusAsync(Guid id, ChangeWorkOrderStatusDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var o = await FindAsync(id, track: true, token);
            var target = r.Status?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!St.IsValid(target)) throw new ValidationFailedException(Messages.WorkOrderStatusInvalid);
            var reason = Trim(r.Reason, 500);
            var from = o.Status;

            if (target != from)
            {
                if (!Transitions.TryGetValue(from, out var allowed) || !(allowed.Forward.Contains(target) || allowed.Back.Contains(target)))
                    throw new ConflictException(string.Format(Messages.WorkOrderTransitionNotAllowed, StatusLabel(from), StatusLabel(target)));
                if (allowed.Back.Contains(target) && reason == null) throw new ValidationFailedException(Messages.WorkOrderBackReasonRequired);
                if (target == St.Approved) await _permissions.DemandAsync(Screen, ScreenAction.Approve, ct: token);
                // اجتياز فحص الجودة إلى «جاهز» اعتماد، والرجوع منه لإعادة العمل رفض، بشاشة الجودة
                if (from == St.Quality) await _permissions.DemandAsync(PermissionCatalog.WorkshopQualityScreenId, target == St.Ready ? ScreenAction.Approve : ScreenAction.Reject, ct: token);
                await WorkOrderRules.EnsureTransitionAsync(_db, o, from, target, token);
            }
            else if (r.StageId == o.StageId) return await GetAsync(id, token);

            Guid? pauseReason = null;
            if (target == St.Paused)
            {
                pauseReason = r.PauseReasonId ?? o.PauseReasonId ?? throw new ValidationFailedException(Messages.WorkOrderPauseReasonRequired);
                if (!await _db.Set<WorkshopPauseReason>().AnyAsync(p => p.Id == pauseReason && p.IsActive, token))
                    throw new ValidationFailedException(Messages.WorkshopPauseReasonNotFound);
            }
            if (r.StageId is Guid stageId && !await _db.Set<WorkshopStage>().AnyAsync(s => s.Id == stageId && s.IsActive && s.Status == target, token))
                throw new ValidationFailedException(Messages.WorkOrderStageInvalid);

            o.Status = target; o.StageId = r.StageId; o.PauseReasonId = pauseReason;
            if (target == St.Ready) o.ReadyAt = SaudiTime.Now;
            else if (from == St.Ready) o.ReadyAt = null;
            AddHistory(o, from, target, r.StageId, pauseReason, reason);
            await _db.SaveChangesAsync(token);
            await LogAsync("WORK_ORDER_STATUS_CHANGED", o, $"{from} → {target}{(reason == null ? string.Empty : " • " + reason)}", token);
            return await GetAsync(id, token);
        }, ct);

    public async Task<WorkOrderDto> CancelAsync(Guid id, WorkshopReasonDto request, CancellationToken ct = default)
    {
        var o = await FindAsync(id, track: true, ct);
        if (!St.Editable.Contains(o.Status)) throw new ConflictException(Messages.WorkOrderNotEditable);
        var reason = Trim(request.Reason, 500) ?? throw new ValidationFailedException(Messages.WorkshopCancelReasonRequired);
        // لا تُترك تكلفة على تحت التنفيذ بلا أمر: القطع المصروفة تُرجَع، وساعات العمل المقيّدة تُحذف (فيُعكس قيدها)، ولا سجل مفتوح
        var issued = await _db.Set<WorkOrderPartIssue>().Where(i => i.WorkOrderId == id)
            .SumAsync(i => (decimal?)(i.Kind == PartIssueKinds.Issue ? i.Quantity : -i.Quantity), ct) ?? 0;
        if (issued > 0.0001m || await _db.Set<WorkOrderTimeLog>().AnyAsync(l => l.WorkOrderId == id && (l.EndedAt == null || l.JournalEntryId != null), ct))
            throw new ConflictException(Messages.WorkOrderCancelHasCosts);
        foreach (var res in await _db.Set<WorkOrderPartReservation>().Where(r => r.WorkOrderId == id && r.Status == PartReservationStatuses.Active).ToListAsync(ct))
            res.Status = PartReservationStatuses.Released;
        // إلغاء أمر مطالبة الضمان يعيد الضمان نشطاً
        if (o.WarrantyId is Guid wid && await _db.Set<WorkshopWarranty>().FirstOrDefaultAsync(w => w.Id == wid && w.ClaimWorkOrderId == id, ct) is { } warranty)
        {
            warranty.Status = WarrantyStatuses.Active; warranty.ClaimWorkOrderId = null;
        }
        var from = o.Status;
        o.Status = St.Cancelled; o.CancelReason = reason; o.StageId = null; o.PauseReasonId = null;
        AddHistory(o, from, St.Cancelled, null, null, reason);
        await _db.SaveChangesAsync(ct);
        await LogAsync("WORK_ORDER_CANCELLED", o, reason, ct);
        return await GetAsync(id, ct);
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var o = await FindAsync(id, track: true, token);
            if (o.Status != St.Received || await _db.Set<WorkOrderStatusHistory>().CountAsync(h => h.WorkOrderId == id, token) > 1)
                throw new ConflictException(Messages.WorkOrderDeleteReceivedOnly);
            // لم يتحرك: لا تقدير أُرسل ولا فحص اكتمل؛ مسوداتهما وأسطره تُحذف معه
            if (await _db.Set<WorkOrderEstimate>().AnyAsync(e => e.WorkOrderId == id && e.Status != EstimateStatuses.Draft, token)
                || await _db.Set<WorkOrderInspection>().AnyAsync(i => i.WorkOrderId == id && i.Status == InspectionStatuses.Completed, token))
                throw new ConflictException(Messages.WorkOrderDeleteReceivedOnly);
            var inspections = await _db.Set<WorkOrderInspection>().Include(i => i.Results).Where(i => i.WorkOrderId == id).ToListAsync(token);
            await DocumentAttachmentService.RemoveForOwnersAsync(_db, WorkshopAttachmentOwnerTypes.InspectionResult,
                inspections.SelectMany(i => i.Results.Select(r => r.Id)).ToList(), token);
            _db.RemoveRange(inspections);
            _db.RemoveRange(await _db.Set<WorkOrderEstimate>().Include(e => e.Lines).Where(e => e.WorkOrderId == id).ToListAsync(token));
            _db.RemoveRange(await _db.Set<WorkOrderPartReservation>().Where(r => r.WorkOrderId == id).ToListAsync(token));
            _db.RemoveRange(await _db.Set<WorkOrderLine>().Where(l => l.WorkOrderId == id).ToListAsync(token));
            var damageIds = o.DamageMarks.Select(d => d.Id).ToList();
            await DocumentAttachmentService.RemoveForOwnersAsync(_db, WorkshopAttachmentOwnerTypes.Damage, damageIds, token);
            await DocumentAttachmentService.RemoveForOwnersAsync(_db, WorkshopAttachmentOwnerTypes.WorkOrder, new[] { id }, token);
            _db.RemoveRange(await _db.Set<WorkOrderStatusHistory>().Where(h => h.WorkOrderId == id).ToListAsync(token));
            if (o.WarrantyId is Guid wid && await _db.Set<WorkshopWarranty>().FirstOrDefaultAsync(w => w.Id == wid && w.ClaimWorkOrderId == id, token) is { } warranty)
            {
                warranty.Status = WarrantyStatuses.Active; warranty.ClaimWorkOrderId = null;
            }
            // الموعد المرتبط يعود مفتوحاً (المركبة لم تُستقبل فعلياً)
            await _db.Set<WorkshopAppointment>().Where(a => a.WorkOrderId == id).ExecuteUpdateAsync(x => x
                .SetProperty(a => a.WorkOrderId, (Guid?)null).SetProperty(a => a.Status, AppointmentStatuses.Confirmed), token);
            _db.RemoveRange(o.DamageMarks);
            _db.Remove(o);
            await _db.SaveChangesAsync(token);
            if (!await _db.Set<WorkOrder>().AnyAsync(x => x.ReceptionId == o.ReceptionId, token)
                && await _db.Set<WorkshopReception>().FirstOrDefaultAsync(x => x.Id == o.ReceptionId, token) is { } reception)
            {
                _db.Remove(reception);
                await _db.SaveChangesAsync(token);
            }
            await LogAsync("WORK_ORDER_DELETED", o, null, token);
        }, ct);

    // ---------- الأضرار ----------

    public async Task<WorkOrderDto> AddDamageAsync(Guid id, SaveDamageMarkDto request, CancellationToken ct = default)
    {
        var o = await FindAsync(id, track: true, ct);
        if (!St.Editable.Contains(o.Status)) throw new ConflictException(Messages.WorkOrderNotEditable);
        if (o.DamageMarks.Count >= MaxDamages) throw new ValidationFailedException(string.Format(Messages.WorkOrderTooManyDamages, MaxDamages));
        var mark = NewDamage(request, o.DamageMarks.Count == 0 ? 1 : o.DamageMarks.Max(d => d.SortOrder) + 1);
        mark.WorkOrderId = o.Id;
        _db.Add(mark);
        await _db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<WorkOrderDto> UpdateDamageAsync(Guid id, Guid damageId, SaveDamageMarkDto request, CancellationToken ct = default)
    {
        var o = await FindAsync(id, track: true, ct);
        if (!St.Editable.Contains(o.Status)) throw new ConflictException(Messages.WorkOrderNotEditable);
        var mark = o.DamageMarks.FirstOrDefault(d => d.Id == damageId) ?? throw new NotFoundException(Messages.WorkOrderDamageNotFound);
        var updated = NewDamage(request, mark.SortOrder);
        mark.Area = updated.Area; mark.DamageType = updated.DamageType; mark.Severity = updated.Severity; mark.Notes = updated.Notes;
        await _db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public Task<WorkOrderDto> DeleteDamageAsync(Guid id, Guid damageId, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var o = await FindAsync(id, track: true, token);
            if (!St.Editable.Contains(o.Status)) throw new ConflictException(Messages.WorkOrderNotEditable);
            var mark = o.DamageMarks.FirstOrDefault(d => d.Id == damageId) ?? throw new NotFoundException(Messages.WorkOrderDamageNotFound);
            await DocumentAttachmentService.RemoveForOwnersAsync(_db, WorkshopAttachmentOwnerTypes.Damage, new[] { damageId }, token);
            _db.Remove(mark);
            await _db.SaveChangesAsync(token);
            return await GetAsync(id, token);
        }, ct);

    private static WorkOrderDamageMark NewDamage(SaveDamageMarkDto d, int order)
    {
        var area = Trim(d.Area, 100) ?? throw new ValidationFailedException(Messages.WorkOrderDamageAreaRequired);
        var type = d.DamageType?.Trim().ToLowerInvariant();
        if (!DamageTypes.IsValid(type)) throw new ValidationFailedException(Messages.WorkOrderDamageTypeInvalid);
        var severity = string.IsNullOrWhiteSpace(d.Severity) ? DamageSeverities.Minor : d.Severity.Trim().ToLowerInvariant();
        if (!DamageSeverities.IsValid(severity)) throw new ValidationFailedException(Messages.WorkOrderDamageTypeInvalid);
        return new WorkOrderDamageMark { Area = area, DamageType = type!, Severity = severity, Notes = Trim(d.Notes, 500), SortOrder = order };
    }

    // ---------- مساعدات ----------

    private async Task ApplyDetailsAsync(WorkOrder o, ServiceVehicle vehicle, decimal odometer, decimal? fuel, string? complaint, string? accessories,
        string? priority, DateTime? promisedAt, Guid? advisorId, string? advisorName, string? notes, CancellationToken ct)
    {
        if (odometer < 0) throw new ValidationFailedException(Messages.WorkOrderOdometerInvalid);
        var reading = decimal.Round(odometer, 0);
        // آخر قراءة من أوامر المركبة الأخرى (لا من هذا الأمر حتى يُصحَّح)
        var floor = o.Id == Guid.Empty ? vehicle.LastOdometer : await OtherReadingsAsync(o, vehicle, ct);
        if (reading < floor) throw new ValidationFailedException(string.Format(Messages.WorkOrderOdometerBackwards, reading, floor));
        if (fuel is < 0 or > 100) throw new ValidationFailedException(Messages.WorkOrderFuelInvalid);
        var p = string.IsNullOrWhiteSpace(priority) ? WorkOrderPriorities.Normal : priority.Trim().ToLowerInvariant();
        if (!WorkOrderPriorities.IsValid(p)) throw new ValidationFailedException(Messages.WorkOrderPriorityInvalid);
        var promised = promisedAt is DateTime pa ? Local(pa) : (DateTime?)null;
        if (promised < o.ReceivedAt) throw new ValidationFailedException(Messages.WorkOrderPromisedBeforeReception);

        o.OdometerIn = reading; o.FuelLevelIn = fuel is decimal f ? decimal.Round(f, 0) : null; o.Complaint = Trim(complaint, 2000);
        o.Accessories = Trim(accessories, 500); o.Priority = p; o.PromisedAt = promised; o.Notes = Trim(notes, 1000);
        o.ServiceAdvisorUserId = advisorId ?? (o.Id == Guid.Empty ? _user.UserId : o.ServiceAdvisorUserId);
        o.ServiceAdvisorName = Trim(advisorName, 200) ?? (advisorId == null && o.Id == Guid.Empty ? _user.Name ?? _user.Email : o.ServiceAdvisorName);
        if (reading > vehicle.LastOdometer) vehicle.LastOdometer = reading;
    }

    private async Task<decimal> OtherReadingsAsync(WorkOrder o, ServiceVehicle vehicle, CancellationToken ct)
        => await _db.Set<WorkOrder>().AsNoTracking().Where(x => x.VehicleId == vehicle.Id && x.Id != o.Id && x.ReceivedAt <= o.ReceivedAt && x.Status != St.Cancelled)
            .Select(x => (decimal?)(x.OdometerOut ?? x.OdometerIn)).MaxAsync(ct) ?? 0;

    private void AddHistory(WorkOrder o, string? from, string to, Guid? stageId, Guid? pauseReasonId, string? reason)
        => _db.Add(new WorkOrderStatusHistory
        {
            WorkOrderId = o.Id, FromStatus = from, ToStatus = to, StageId = stageId, PauseReasonId = pauseReasonId, Reason = reason,
            ChangedBy = _user.Name ?? _user.Email, ChangedByUserId = _user.UserId, ChangedAt = SaudiTime.Now,
        });

    private async Task<WorkOrder> FindAsync(Guid id, bool track, CancellationToken ct)
    {
        var q = _db.Set<WorkOrder>().Include(o => o.DamageMarks).AsQueryable();
        if (!track) q = q.AsNoTracking();
        return await q.FirstOrDefaultAsync(o => o.Id == id, ct) ?? throw new NotFoundException(Messages.WorkOrderNotFound);
    }

    internal static List<string> NextStatuses(string status)
        => Transitions.TryGetValue(status, out var t) ? t.Forward.Concat(t.Back).ToList() : new List<string>();

    private static string StatusLabel(string status)
        => Messages.ResourceManager.GetString("WorkOrderStatus_" + status, Messages.Culture) ?? status;

    // ---------- العرض ----------

    private async Task<List<WorkshopReceptionDto>> ReceptionDtosAsync(List<WorkshopReception> list, CancellationToken ct)
    {
        if (list.Count == 0) return new();
        var ids = list.Select(r => r.Id).ToList();
        var customerIds = list.Select(r => r.CustomerId).Distinct().ToList();
        var customers = await _db.Set<Customer>().AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.NameAr, ct);
        var whIds = list.Where(r => r.WarehouseId != null).Select(r => r.WarehouseId!.Value).Distinct().ToList();
        var warehouses = await _db.Set<Warehouse>().AsNoTracking().Where(w => whIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.NameAr, ct);
        var orders = await DtosAsync(await _db.Set<WorkOrder>().AsNoTracking().Include(o => o.DamageMarks).Where(o => ids.Contains(o.ReceptionId))
            .OrderBy(o => o.OrderNumber).ToListAsync(ct), withHistory: false, ct);
        return list.Select(r => new WorkshopReceptionDto
        {
            Id = r.Id, ReceptionNumber = r.ReceptionNumber, CustomerId = r.CustomerId, CustomerName = customers.GetValueOrDefault(r.CustomerId),
            WarehouseId = r.WarehouseId, WarehouseName = r.WarehouseId is Guid w ? warehouses.GetValueOrDefault(w) : null, ReceivedAt = r.ReceivedAt,
            ContactName = r.ContactName, ContactPhone = r.ContactPhone, Notes = r.Notes, CreatedBy = r.CreatedBy, CreatedAt = r.CreatedAt,
            Orders = orders.Where(o => o.ReceptionId == r.Id).ToList(),
        }).ToList();
    }

    private async Task<List<WorkOrderDto>> DtosAsync(List<WorkOrder> list, bool withHistory, CancellationToken ct)
    {
        if (list.Count == 0) return new();
        var ids = list.Select(o => o.Id).ToList();
        var receptionIds = list.Select(o => o.ReceptionId).Distinct().ToList();
        var receptions = await _db.Set<WorkshopReception>().AsNoTracking().Where(r => receptionIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.ReceptionNumber, ct);
        var vehicleIds = list.Select(o => o.VehicleId).Distinct().ToList();
        var vehicles = await _db.Set<ServiceVehicle>().AsNoTracking().Where(v => vehicleIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
        var whIds = list.Where(o => o.WarehouseId != null).Select(o => o.WarehouseId!.Value).Distinct().ToList();
        var warehouses = await _db.Set<Warehouse>().AsNoTracking().Where(w => whIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.NameAr, ct);
        var stages = await _db.Set<WorkshopStage>().AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.NameAr, ct);
        var pauses = await _db.Set<WorkshopPauseReason>().AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.NameAr, ct);
        var attachments = await _db.Set<DocumentAttachment>().AsNoTracking()
            .Where(a => a.OwnerType == WorkshopAttachmentOwnerTypes.WorkOrder && ids.Contains(a.OwnerId))
            .GroupBy(a => a.OwnerId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var damageIds = list.SelectMany(o => o.DamageMarks.Select(d => d.Id)).ToList();
        var photos = damageIds.Count == 0 ? new Dictionary<Guid, int>() : await _db.Set<DocumentAttachment>().AsNoTracking()
            .Where(a => a.OwnerType == WorkshopAttachmentOwnerTypes.Damage && damageIds.Contains(a.OwnerId))
            .GroupBy(a => a.OwnerId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var history = withHistory
            ? (await _db.Set<WorkOrderStatusHistory>().AsNoTracking().Where(h => ids.Contains(h.WorkOrderId)).ToListAsync(ct))
                .OrderBy(h => h.ChangedAt).ThenBy(h => h.CreatedAt).ToList()
            : new List<WorkOrderStatusHistory>();
        var bayIds = list.Where(o => o.BayId != null).Select(o => o.BayId!.Value).Distinct().ToList();
        var bays = bayIds.Count == 0 ? new Dictionary<Guid, string>() : await _db.Set<WorkshopBay>().AsNoTracking().Where(b => bayIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.NameAr, ct);
        var techIds = list.Where(o => o.AssignedTechnicianId != null).Select(o => o.AssignedTechnicianId!.Value).Distinct().ToList();
        var techs = techIds.Count == 0 ? new Dictionary<Guid, string>() : await _db.Set<Employee>().AsNoTracking().Where(e => techIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.NameAr, ct);
        var now = SaudiTime.Now;

        return list.Select(o =>
        {
            var v = vehicles.GetValueOrDefault(o.VehicleId);
            return new WorkOrderDto
            {
                Id = o.Id, OrderNumber = o.OrderNumber, ReceptionId = o.ReceptionId, ReceptionNumber = receptions.GetValueOrDefault(o.ReceptionId),
                WarehouseId = o.WarehouseId, WarehouseName = o.WarehouseId is Guid w ? warehouses.GetValueOrDefault(w) : null,
                CustomerId = o.CustomerId, CustomerName = o.CustomerName, CustomerPhone = o.CustomerPhone, VehicleId = o.VehicleId,
                VehicleLabel = v == null ? null : $"{v.Make} {v.Model} {v.Year}".Trim(), Vin = v?.Vin, PlateNumber = o.PlateSnapshot ?? v?.PlateNumber,
                Status = o.Status, StageId = o.StageId, StageName = o.StageId is Guid s ? stages.GetValueOrDefault(s) : null,
                PauseReasonId = o.PauseReasonId, PauseReasonName = o.PauseReasonId is Guid p ? pauses.GetValueOrDefault(p) : null, Priority = o.Priority,
                OdometerIn = o.OdometerIn, OdometerOut = o.OdometerOut, FuelLevelIn = o.FuelLevelIn, Complaint = o.Complaint, Accessories = o.Accessories,
                ServiceAdvisorUserId = o.ServiceAdvisorUserId, ServiceAdvisorName = o.ServiceAdvisorName, ReceivedAt = o.ReceivedAt, PromisedAt = o.PromisedAt,
                IsOverdue = o.PromisedAt is DateTime due && due < now && St.Editable.Contains(o.Status) && o.Status != St.Ready,
                ReadyAt = o.ReadyAt, DeliveredAt = o.DeliveredAt, Notes = o.Notes, CancelReason = o.CancelReason, CreatedBy = o.CreatedBy, CreatedAt = o.CreatedAt,
                IsWarrantyJob = o.IsWarrantyJob, WarrantyId = o.WarrantyId, InvoiceId = o.InvoiceId, InvoiceNumber = o.InvoiceNumber, InvoicedAt = o.InvoicedAt,
                DeliveredTo = o.DeliveredTo,
                BayId = o.BayId, BayName = o.BayId is Guid bay ? bays.GetValueOrDefault(bay) : null, AssignedTechnicianId = o.AssignedTechnicianId,
                AssignedTechnicianName = o.AssignedTechnicianId is Guid tech ? techs.GetValueOrDefault(tech) : null, PlannedStartAt = o.PlannedStartAt, PlannedEndAt = o.PlannedEndAt,
                AttachmentCount = attachments.GetValueOrDefault(o.Id), NextStatuses = NextStatuses(o.Status),
                Damages = o.DamageMarks.OrderBy(d => d.SortOrder).Select(d => new DamageMarkDto
                {
                    Id = d.Id, Area = d.Area, DamageType = d.DamageType, Severity = d.Severity, Notes = d.Notes, SortOrder = d.SortOrder,
                    PhotoCount = photos.GetValueOrDefault(d.Id),
                }).ToList(),
                History = history.Where(h => h.WorkOrderId == o.Id).Select(h => new WorkOrderStatusHistoryDto
                {
                    Id = h.Id, FromStatus = h.FromStatus, ToStatus = h.ToStatus, StageId = h.StageId,
                    StageName = h.StageId is Guid hs ? stages.GetValueOrDefault(hs) : null,
                    PauseReasonName = h.PauseReasonId is Guid hp ? pauses.GetValueOrDefault(hp) : null, Reason = h.Reason, ChangedBy = h.ChangedBy, ChangedAt = h.ChangedAt,
                }).ToList(),
            };
        }).ToList();
    }

    private Task LogAsync(string action, WorkOrder o, string? details, CancellationToken ct)
        => _audit.LogAsync(action, nameof(WorkOrder), o.Id.ToString(), $"{o.OrderNumber}{(details == null ? string.Empty : " • " + details)}", ct);

    private static DateTime Local(DateTime value) => value.Kind == DateTimeKind.Utc
        ? SaudiTime.FromUtc(value)
        : DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

    private static string? Trim(string? value, int max)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length > max ? v[..max] : v;
    }
}
