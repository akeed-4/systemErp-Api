using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using A = ERP.Core.Models.Accounting.LineApprovalStatuses;
using RSt = ERP.Core.Models.Accounting.PartReservationStatuses;
using St = ERP.Core.Models.Accounting.WorkOrderStatuses;
using XSt = ERP.Core.Models.Accounting.ExternalServiceStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// تنفيذ أمر العمل (W3) عبر الخدمات القائمة (المخزون <see cref="IInventoryService"/> والقيود <see cref="IAccountingPostingService"/>). القواعد:
/// <list type="bullet">
/// <item>كل أثر تنفيذ (عمل الفني، الصرف، الخدمة الخارجية) على سطر قابل للتنفيذ (<see cref="WorkOrderRules.EnsureExecutableAsync"/>) وأمر
/// معتمد أو قيد التنفيذ أو متوقف أو في الجودة. التوقف بسبب نشط ولا يحتاج سطراً.</item>
/// <item>الفني موظف نشط بسجل مفتوح واحد؛ الإغلاق يحسب الدقائق والتكلفة بلقطة تكلفته المعيارية في يوم البداية، ويقيّد العمل: مدين
/// تحت التنفيذ / دائن العمالة المحمّلة. الحذف يعكس القيد ما دام الأمر قبل الفوترة.</item>
/// <item>الحجز لسطر قطعة ضمن كميته الباقية ومتاح الورشة (رصيد المستودع − حجوزات الأوامر الأخرى السارية)؛ ينتهي بمدة الإعداد.</item>
/// <item>الصرف لسطر قطعة ضمن كميته الباقية ومتاح الورشة (حجز السطر نفسه متاح له): حركة `AdjustmentOut` بمصدر
/// <see cref="SourceType"/> بتكلفتها الفعلية، وقيد مدين تحت التنفيذ / دائن مخزون المستودع (لا قيد في المخزون الدوري)، ويستهلك حجوزات
/// السطر. الإرجاع على صرف بعينه ضمن الباقي منه، بتكلفته وتشغيلاته، وقيده عكس الصرف.</item>
/// <item>الخدمة الخارجية لسطر خدمة خارجية بمورد؛ الإنجاز بتكلفته الفعلية وفاتورة الشراء المرتبطة (المرحّلة لنفس المورد). لا قيد هنا:
/// التكلفة من فاتورة المورد.</item>
/// </list>
/// </summary>
public class WorkOrderExecutionService : IWorkOrderExecutionService
{
    public const string SourceType = "workshop_work_order";
    private const string TimeSourceType = "workshop_time_log";
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(15);
    private static readonly string[] ExecutionStatuses = { St.Approved, St.InProgress, St.Paused, St.Quality };

    private readonly ErpDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly IAccountingPostingService _posting;
    private readonly IEmployeeCostRateService _rates;
    private readonly ITransactionRunner _tx;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;
    private readonly IAuditService _audit;

    public WorkOrderExecutionService(ErpDbContext db, IInventoryService inventory, IAccountingPostingService posting, IEmployeeCostRateService rates,
        ITransactionRunner tx, ICurrentUser user, IPermissionService permissions, IAuditService audit)
    {
        _db = db; _inventory = inventory; _posting = posting; _rates = rates; _tx = tx; _user = user; _permissions = permissions; _audit = audit;
    }

    private string? By => _user.Name ?? _user.Email;

    // ---------- العرض ----------

    public async Task<WorkOrderExecutionDto> GetAsync(Guid workOrderId, CancellationToken ct = default)
    {
        var o = await _db.Set<WorkOrder>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == workOrderId, ct) ?? throw new NotFoundException(Messages.WorkOrderNotFound);
        var cost = await _permissions.HasPermissionAsync(PermissionCatalog.WorkshopCostViewScreenId, ScreenAction.View, ct);
        var lines = await _db.Set<WorkOrderLine>().AsNoTracking().Where(l => l.WorkOrderId == o.Id).ToDictionaryAsync(l => l.Id, ct);
        var logs = await _db.Set<WorkOrderTimeLog>().AsNoTracking().Where(l => l.WorkOrderId == o.Id).OrderBy(l => l.StartedAt).ToListAsync(ct);
        var reservations = await _db.Set<WorkOrderPartReservation>().AsNoTracking().Where(r => r.WorkOrderId == o.Id).OrderBy(r => r.CreatedAt).ToListAsync(ct);
        var issues = await _db.Set<WorkOrderPartIssue>().AsNoTracking().Where(i => i.WorkOrderId == o.Id).OrderBy(i => i.Date).ThenBy(i => i.CreatedAt).ToListAsync(ct);
        var externals = await _db.Set<WorkOrderExternalService>().AsNoTracking().Where(x => x.WorkOrderId == o.Id).OrderBy(x => x.CreatedAt).ToListAsync(ct);
        var executable = await WorkOrderRules.ExecutableAsync(_db, o.Id, ct);
        var now = SaudiTime.Now;

        var productIds = lines.Values.Select(l => l.ProductId).Distinct().ToList();
        var skus = await _db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Sku, ct);
        var whIds = reservations.Select(r => r.WarehouseId).Concat(issues.Select(i => i.WarehouseId)).Distinct().ToList();
        if (o.WarehouseId is Guid owh && !whIds.Contains(owh)) whIds.Add(owh);
        var warehouses = await _db.Set<Warehouse>().AsNoTracking().Where(w => whIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.NameAr, ct);
        var empIds = logs.Select(l => l.EmployeeId).Distinct().ToList();
        var employees = await _db.Set<Employee>().AsNoTracking().Where(e => empIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.NameAr, ct);
        var pauseIds = logs.Where(l => l.PauseReasonId != null).Select(l => l.PauseReasonId!.Value).Distinct().ToList();
        var pauses = await _db.Set<WorkshopPauseReason>().AsNoTracking().Where(p => pauseIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.NameAr, ct);
        var supplierIds = externals.Select(x => x.SupplierId).Distinct().ToList();
        var suppliers = await _db.Set<Supplier>().AsNoTracking().Where(s => supplierIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.NameAr, ct);
        var invoiceIds = externals.Where(x => x.PurchaseInvoiceId != null).Select(x => x.PurchaseInvoiceId!.Value).ToList();
        var invoices = await _db.Set<PurchaseInvoice>().AsNoTracking().Where(i => invoiceIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.InvoiceNumber, ct);

        var parts = new List<PartLineStockDto>();
        var warehouse = o.WarehouseId ?? (await WarehouseStocks.ResolveAsync(_db, null, ct)).Id;
        foreach (var l in lines.Values.Where(l => l.Kind == WorkOrderLineKinds.Part && l.ApprovalStatus != A.Rejected).OrderBy(l => l.SortOrder))
        {
            var stock = await WarehouseStocks.QuantityAsync(_db, l.ProductId, warehouse, ct);
            var others = await ActiveReservedAsync(l.ProductId, warehouse, o.Id, now, ct);
            parts.Add(new PartLineStockDto
            {
                LineId = l.Id, Description = l.Description, ProductId = l.ProductId, ProductSku = skus.GetValueOrDefault(l.ProductId), Required = l.Quantity,
                Reserved = reservations.Where(r => r.LineId == l.Id && IsActive(r, now)).Sum(r => r.Quantity - r.ConsumedQuantity),
                IssuedNet = NetIssued(issues, l.Id), WarehouseStock = stock, Available = Math.Max(0, stock - others), CanExecute = executable.Contains(l.Id),
            });
        }
        string? Desc(Guid? id) => id is Guid g && lines.TryGetValue(g, out var l) ? l.Description : null;

        var labor = logs.Where(l => l.Kind == TimeLogKinds.Work && l.EndedAt != null).Sum(l => l.Cost);
        var partsCost = issues.Sum(i => i.Kind == PartIssueKinds.Issue ? i.TotalCost : -i.TotalCost);
        var external = externals.Where(x => x.Status == XSt.Completed).Sum(x => x.ActualCost ?? 0);
        return new WorkOrderExecutionDto
        {
            WorkOrderId = o.Id, CanViewCost = cost,
            TimeLogs = logs.Select(l => LogDto(l, o.OrderNumber, Desc(l.LineId), employees.GetValueOrDefault(l.EmployeeId),
                l.PauseReasonId is Guid p ? pauses.GetValueOrDefault(p) : null, cost)).ToList(),
            Parts = parts,
            Reservations = reservations.Select(r => new PartReservationDto
            {
                Id = r.Id, LineId = r.LineId, LineDescription = Desc(r.LineId), ProductId = r.ProductId, ProductSku = skus.GetValueOrDefault(r.ProductId),
                WarehouseId = r.WarehouseId, WarehouseName = warehouses.GetValueOrDefault(r.WarehouseId), Quantity = r.Quantity, ConsumedQuantity = r.ConsumedQuantity,
                Status = r.Status == RSt.Active && !IsActive(r, now) ? "expired" : r.Status, ExpiresAt = r.ExpiresAt, CreatedBy = r.CreatedBy, CreatedAt = r.CreatedAt,
            }).ToList(),
            Issues = issues.Select(i => new PartIssueDto
            {
                Id = i.Id, LineId = i.LineId, LineDescription = Desc(i.LineId), ProductId = i.ProductId, ProductSku = skus.GetValueOrDefault(i.ProductId),
                WarehouseId = i.WarehouseId, WarehouseName = warehouses.GetValueOrDefault(i.WarehouseId), Kind = i.Kind, ReturnOfId = i.ReturnOfId,
                Quantity = i.Quantity, UnitCost = cost ? i.UnitCost : null, TotalCost = cost ? i.TotalCost : null, Date = i.Date, Posted = i.JournalEntryId != null,
                ReturnableQuantity = i.Kind == PartIssueKinds.Issue ? i.Quantity - issues.Where(x => x.ReturnOfId == i.Id).Sum(x => x.Quantity) : 0,
                Notes = i.Notes, CreatedBy = i.CreatedBy,
            }).ToList(),
            ExternalServices = externals.Select(x => new ExternalServiceDto
            {
                Id = x.Id, LineId = x.LineId, LineDescription = Desc(x.LineId), SupplierId = x.SupplierId, SupplierName = suppliers.GetValueOrDefault(x.SupplierId),
                Description = x.Description, EstimatedCost = cost ? x.EstimatedCost : null, ActualCost = cost ? x.ActualCost : null, Status = x.Status,
                SentAt = x.SentAt, CompletedAt = x.CompletedAt, PurchaseInvoiceId = x.PurchaseInvoiceId,
                PurchaseInvoiceNumber = x.PurchaseInvoiceId is Guid inv ? invoices.GetValueOrDefault(inv) : null, Notes = x.Notes, CancelReason = x.CancelReason,
                CreatedBy = x.CreatedBy,
            }).ToList(),
            LaborCost = cost ? labor : null, PartsCost = cost ? partsCost : null, ExternalCost = cost ? external : null,
            TotalCost = cost ? labor + partsCost + external : null,
        };
    }

    public async Task<List<TimeLogDto>> OpenLogsAsync(Guid? employeeId, CancellationToken ct = default)
    {
        var q = _db.Set<WorkOrderTimeLog>().AsNoTracking().Where(l => l.EndedAt == null);
        if (employeeId is Guid e) q = q.Where(l => l.EmployeeId == e);
        var logs = await q.OrderBy(l => l.StartedAt).ToListAsync(ct);
        var orderIds = logs.Select(l => l.WorkOrderId).Distinct().ToList();
        var orders = await _db.Set<WorkOrder>().AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.OrderNumber, ct);
        var lineIds = logs.Where(l => l.LineId != null).Select(l => l.LineId!.Value).ToList();
        var lines = await _db.Set<WorkOrderLine>().AsNoTracking().Where(l => lineIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Description, ct);
        var empIds = logs.Select(l => l.EmployeeId).Distinct().ToList();
        var employees = await _db.Set<Employee>().AsNoTracking().Where(x => empIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.NameAr, ct);
        var pauseIds = logs.Where(l => l.PauseReasonId != null).Select(l => l.PauseReasonId!.Value).Distinct().ToList();
        var pauses = await _db.Set<WorkshopPauseReason>().AsNoTracking().Where(p => pauseIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.NameAr, ct);
        return logs.Select(l => LogDto(l, orders.GetValueOrDefault(l.WorkOrderId), l.LineId is Guid li ? lines.GetValueOrDefault(li) : null,
            employees.GetValueOrDefault(l.EmployeeId), l.PauseReasonId is Guid p ? pauses.GetValueOrDefault(p) : null, false)).ToList();
    }

    private static TimeLogDto LogDto(WorkOrderTimeLog l, string? order, string? line, string? employee, string? pause, bool cost) => new()
    {
        Id = l.Id, WorkOrderId = l.WorkOrderId, OrderNumber = order, LineId = l.LineId, LineDescription = line, EmployeeId = l.EmployeeId, EmployeeName = employee,
        Kind = l.Kind, PauseReasonId = l.PauseReasonId, PauseReasonName = pause, StartedAt = l.StartedAt, EndedAt = l.EndedAt, Minutes = l.Minutes,
        CostPerHour = cost ? l.CostPerHour : null, Cost = cost ? l.Cost : null, Posted = l.JournalEntryId != null, Notes = l.Notes, CreatedBy = l.CreatedBy,
    };

    // ---------- ساعات الفنيين ----------

    public Task<TimeLogDto> StartLogAsync(Guid workOrderId, SaveTimeLogDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var o = await ExecutingOrderAsync(workOrderId, token);
            var kind = r.Kind?.Trim().ToLowerInvariant();
            if (!TimeLogKinds.IsValid(kind)) throw new ValidationFailedException(Messages.TimeLogKindInvalid);
            var employee = await _db.Set<Employee>().AsNoTracking().Where(e => e.Id == r.EmployeeId && e.Status == EmployeeStatuses.Active).Select(e => e.Id)
                .FirstOrDefaultAsync(token);
            if (employee == Guid.Empty) throw new ValidationFailedException(Messages.TimeLogTechnicianNotFound);
            Guid? lineId = null, pause = null;
            if (kind == TimeLogKinds.Work)
            {
                var line = await _db.Set<WorkOrderLine>().FirstOrDefaultAsync(l => l.Id == r.LineId && l.WorkOrderId == o.Id, token)
                    ?? throw new ValidationFailedException(Messages.TimeLogLineRequired);
                if (line.Kind != WorkOrderLineKinds.Labor) throw new ValidationFailedException(Messages.TimeLogLaborOnly);
                await WorkOrderRules.EnsureExecutableAsync(_db, o.Id, line.Id, token);
                if (line.ExecutionStatus == LineExecutionStatuses.NotStarted) line.ExecutionStatus = LineExecutionStatuses.InProgress;
                lineId = line.Id;
            }
            else
            {
                pause = r.PauseReasonId ?? throw new ValidationFailedException(Messages.WorkOrderPauseReasonRequired);
                if (!await _db.Set<WorkshopPauseReason>().AnyAsync(p => p.Id == pause && p.IsActive, token))
                    throw new ValidationFailedException(Messages.WorkshopPauseReasonNotFound);
            }
            var now = SaudiTime.Now;
            var start = r.StartedAt is DateTime s ? Local(s) : now;
            if (start > now + FutureTolerance) throw new ValidationFailedException(Messages.TimeLogInFuture);
            if (start < o.ReceivedAt) throw new ValidationFailedException(Messages.TimeLogBeforeReception);

            // سجل مفتوح واحد للفني، ولا تداخل مع سجلاته المغلقة
            await _db.Set<Employee>().Where(e => e.Id == employee).ExecuteUpdateAsync(x => x.SetProperty(e => e.Status, e => e.Status), token);
            var end = r.EndedAt is DateTime en ? Local(en) : (DateTime?)null;
            if (end == null && await _db.Set<WorkOrderTimeLog>().AnyAsync(l => l.EmployeeId == employee && l.EndedAt == null, token))
                throw new ConflictException(Messages.TimeLogAlreadyOpen);
            var log = new WorkOrderTimeLog
            {
                WorkOrderId = o.Id, LineId = lineId, EmployeeId = employee, Kind = kind!, PauseReasonId = pause, StartedAt = start, Notes = Trim(r.Notes, 500),
                CreatedBy = By,
            };
            await EnsureNoOverlapAsync(log, end ?? now, token);
            _db.Add(log);
            if (end is DateTime finish) await CloseAsync(o, log, finish, now, token);
            await _db.SaveChangesAsync(token);
            await LogAsync("WORK_ORDER_TIME_STARTED", o, $"{kind} • {employee}", token);
            return (await GetAsync(o.Id, token)).TimeLogs.First(x => x.Id == log.Id);
        }, ct);

    public Task<TimeLogDto> StopLogAsync(Guid workOrderId, Guid logId, StopTimeLogDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var o = await _db.Set<WorkOrder>().FirstOrDefaultAsync(x => x.Id == workOrderId, token) ?? throw new NotFoundException(Messages.WorkOrderNotFound);
            if (!St.Editable.Contains(o.Status)) throw new ConflictException(Messages.WorkOrderNotEditable);
            var log = await _db.Set<WorkOrderTimeLog>().FirstOrDefaultAsync(l => l.Id == logId && l.WorkOrderId == o.Id, token)
                ?? throw new NotFoundException(Messages.TimeLogNotFound);
            if (log.EndedAt != null) throw new ConflictException(Messages.TimeLogClosed);
            var now = SaudiTime.Now;
            if (r.Notes != null) log.Notes = Trim(r.Notes, 500);
            await CloseAsync(o, log, r.EndedAt is DateTime e ? Local(e) : now, now, token);
            await _db.SaveChangesAsync(token);
            await LogAsync("WORK_ORDER_TIME_STOPPED", o, $"{log.Minutes:0} min • {log.Cost:0.##}", token);
            return (await GetAsync(o.Id, token)).TimeLogs.First(x => x.Id == log.Id);
        }, ct);

    public Task DeleteLogAsync(Guid workOrderId, Guid logId, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var o = await _db.Set<WorkOrder>().FirstOrDefaultAsync(x => x.Id == workOrderId, token) ?? throw new NotFoundException(Messages.WorkOrderNotFound);
            if (!St.Editable.Contains(o.Status)) throw new ConflictException(Messages.WorkOrderNotEditable);
            var log = await _db.Set<WorkOrderTimeLog>().FirstOrDefaultAsync(l => l.Id == logId && l.WorkOrderId == o.Id, token)
                ?? throw new NotFoundException(Messages.TimeLogNotFound);
            if (log.JournalEntryId is Guid entry) await _posting.ReverseAsync(entry, $"حذف ساعات فني على أمر العمل {o.OrderNumber}", token);
            _db.Remove(log);
            await _db.SaveChangesAsync(token);
            await LogAsync("WORK_ORDER_TIME_DELETED", o, $"{log.Kind} • {log.Minutes:0} min", token);
        }, ct);

    private async Task CloseAsync(WorkOrder o, WorkOrderTimeLog log, DateTime end, DateTime now, CancellationToken ct)
    {
        if (end > now + FutureTolerance) throw new ValidationFailedException(Messages.TimeLogInFuture);
        if (end <= log.StartedAt) throw new ValidationFailedException(Messages.TimeLogEndBeforeStart);
        if ((end - log.StartedAt).TotalHours > 24) throw new ValidationFailedException(Messages.TimeLogTooLong);
        await EnsureNoOverlapAsync(log, end, ct);
        log.EndedAt = end;
        log.Minutes = decimal.Round((decimal)(end - log.StartedAt).TotalMinutes, 0);
        log.CostPerHour = await _rates.RateAtAsync(log.EmployeeId, EmployeeCostPurposes.Technician, log.StartedAt, ct) ?? 0;
        log.Cost = decimal.Round(log.Minutes / 60m * log.CostPerHour, 2);
        if (log.Kind != TimeLogKinds.Work || log.Cost <= 0) return;

        var (wip, absorbed) = await LaborAccountsAsync(ct);
        var note = $"عمالة فني على أمر العمل {o.OrderNumber} ({log.Minutes:0} دقيقة)";
        var posted = await _posting.PostAsync(new GenericPostingRequest
        {
            Date = end.Date, Description = note, SourceType = TimeSourceType, SourceId = log.Id, SourceNumber = o.OrderNumber, WarehouseId = o.WarehouseId,
            Lines = new List<PostingLine> { new(wip, log.Cost, 0, note), new(absorbed, 0, log.Cost, note) },
        }, ct);
        log.JournalEntryId = posted.JournalEntryId;
    }

    private async Task EnsureNoOverlapAsync(WorkOrderTimeLog log, DateTime end, CancellationToken ct)
    {
        if (await _db.Set<WorkOrderTimeLog>().AnyAsync(l => l.EmployeeId == log.EmployeeId && l.Id != log.Id
                                                            && l.StartedAt < end && (l.EndedAt ?? DateTime.MaxValue) > log.StartedAt, ct))
            throw new ConflictException(Messages.TimeLogOverlap);
    }

    // ---------- القطع ----------

    public Task<WorkOrderExecutionDto> ReserveAsync(Guid workOrderId, ReservePartDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var o = await _db.Set<WorkOrder>().FirstOrDefaultAsync(x => x.Id == workOrderId, token) ?? throw new NotFoundException(Messages.WorkOrderNotFound);
            if (!St.Editable.Contains(o.Status)) throw new ConflictException(Messages.WorkOrderNotEditable);
            var line = await PartLineAsync(o, r.LineId, token);
            if (line.ApprovalStatus == A.Rejected) throw new ConflictException(Messages.PartLineRejected);
            var quantity = decimal.Round(r.Quantity, 4);
            if (quantity <= 0) throw new ValidationFailedException(Messages.WorkOrderLineQuantityInvalid);
            var warehouse = await WarehouseOfAsync(o, r.WarehouseId, token);
            await LockProductAsync(line.ProductId, token);
            var now = SaudiTime.Now;
            var issues = await _db.Set<WorkOrderPartIssue>().AsNoTracking().Where(i => i.LineId == line.Id).ToListAsync(token);
            var reserved = (await _db.Set<WorkOrderPartReservation>().AsNoTracking().Where(x => x.LineId == line.Id).ToListAsync(token))
                .Where(x => IsActive(x, now)).Sum(x => x.Quantity - x.ConsumedQuantity);
            if (NetIssued(issues, line.Id) + reserved + quantity > line.Quantity + 0.0001m)
                throw new ValidationFailedException(string.Format(Messages.PartExceedsLine, line.Description, line.Quantity));
            var available = await WarehouseStocks.QuantityAsync(_db, line.ProductId, warehouse, token) - await ActiveReservedAsync(line.ProductId, warehouse, o.Id, now, token)
                            - reserved;
            if (quantity > available + 0.0001m) throw new ConflictException(string.Format(Messages.PartNotAvailable, line.Description, Math.Max(0, available)));
            var days = (await _db.Set<WorkshopSettings>().AsNoTracking().FirstOrDefaultAsync(token))?.ReservationHoldDays ?? 7;
            _db.Add(new WorkOrderPartReservation
            {
                WorkOrderId = o.Id, LineId = line.Id, ProductId = line.ProductId, WarehouseId = warehouse, Quantity = quantity, Status = RSt.Active,
                ExpiresAt = days > 0 ? now.AddDays(days) : null, CreatedBy = By,
            });
            await _db.SaveChangesAsync(token);
            await LogAsync("WORK_ORDER_PART_RESERVED", o, $"{line.Description} • {quantity:0.####}", token);
            return await GetAsync(o.Id, token);
        }, ct);

    public async Task<WorkOrderExecutionDto> ReleaseAsync(Guid workOrderId, Guid reservationId, CancellationToken ct = default)
    {
        var o = await _db.Set<WorkOrder>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == workOrderId, ct) ?? throw new NotFoundException(Messages.WorkOrderNotFound);
        var res = await _db.Set<WorkOrderPartReservation>().FirstOrDefaultAsync(x => x.Id == reservationId && x.WorkOrderId == o.Id, ct)
            ?? throw new NotFoundException(Messages.PartReservationNotFound);
        if (res.Status != RSt.Active) throw new ConflictException(Messages.PartReservationNotActive);
        res.Status = RSt.Released;
        await _db.SaveChangesAsync(ct);
        await LogAsync("WORK_ORDER_PART_RELEASED", o, $"{res.Quantity - res.ConsumedQuantity:0.####}", ct);
        return await GetAsync(o.Id, ct);
    }

    public Task<WorkOrderExecutionDto> IssueAsync(Guid workOrderId, IssuePartDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var o = await ExecutingOrderAsync(workOrderId, token);
            var line = await PartLineAsync(o, r.LineId, token);
            await WorkOrderRules.EnsureExecutableAsync(_db, o.Id, line.Id, token);
            var quantity = decimal.Round(r.Quantity, 4);
            if (quantity <= 0) throw new ValidationFailedException(Messages.WorkOrderLineQuantityInvalid);
            var date = r.Date?.Date ?? SaudiTime.Today;
            if (date > SaudiTime.Today) throw new ValidationFailedException(Messages.TimeLogInFuture);
            var warehouse = await WarehouseOfAsync(o, r.WarehouseId, token);
            await LockProductAsync(line.ProductId, token);
            var now = SaudiTime.Now;
            var issues = await _db.Set<WorkOrderPartIssue>().AsNoTracking().Where(i => i.LineId == line.Id).ToListAsync(token);
            if (NetIssued(issues, line.Id) + quantity > line.Quantity + 0.0001m)
                throw new ValidationFailedException(string.Format(Messages.PartExceedsLine, line.Description, line.Quantity));
            // متاح الورشة: حجوزات هذا الأمر متاحة له
            var available = await WarehouseStocks.QuantityAsync(_db, line.ProductId, warehouse, token) - await ActiveReservedAsync(line.ProductId, warehouse, o.Id, now, token);
            if (quantity > available + 0.0001m) throw new ConflictException(string.Format(Messages.PartNotAvailable, line.Description, Math.Max(0, available)));

            var issue = new WorkOrderPartIssue
            {
                WorkOrderId = o.Id, LineId = line.Id, ProductId = line.ProductId, WarehouseId = warehouse, Kind = PartIssueKinds.Issue, Quantity = quantity, Date = date,
                Notes = Trim(r.Notes, 500), CreatedBy = By,
            };
            _db.Add(issue);
            var note = $"صرف قطعة لأمر العمل {o.OrderNumber}: {line.Description}";
            var moved = await _inventory.RecordMovementAsync(new RecordStockMovementDto
            {
                ItemId = line.ProductId, WarehouseId = warehouse, Quantity = quantity, Type = StockMovementType.AdjustmentOut, ReferenceNumber = o.OrderNumber,
                Date = date, Notes = note, SourceType = SourceType, SourceId = o.Id, SourceLineId = issue.Id,
            }, token);
            issue.StockMovementId = moved.Id; issue.UnitCost = moved.UnitCost; issue.TotalCost = DocumentPricing.Round(-moved.ValueChange);
            issue.JournalEntryId = await PostPartsAsync(o, warehouse, issue.TotalCost, date, note, issue.Id, toWip: true, token);

            // الصرف يستهلك حجوزات السطر الأقدم أولاً
            var left = quantity;
            foreach (var res in (await _db.Set<WorkOrderPartReservation>().Where(x => x.LineId == line.Id && x.Status == RSt.Active).OrderBy(x => x.CreatedAt).ToListAsync(token))
                         .Where(x => IsActive(x, now)))
            {
                if (left <= 0) break;
                var take = Math.Min(left, res.Quantity - res.ConsumedQuantity);
                res.ConsumedQuantity += take; left -= take;
                if (res.ConsumedQuantity >= res.Quantity) res.Status = RSt.Consumed;
            }
            if (line.ExecutionStatus == LineExecutionStatuses.NotStarted) line.ExecutionStatus = LineExecutionStatuses.InProgress;
            await _db.SaveChangesAsync(token);
            await LogAsync("WORK_ORDER_PART_ISSUED", o, $"{line.Description} • {quantity:0.####} • {issue.TotalCost:0.##}", token);
            return await GetAsync(o.Id, token);
        }, ct);

    public Task<WorkOrderExecutionDto> ReturnAsync(Guid workOrderId, ReturnPartDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var o = await _db.Set<WorkOrder>().FirstOrDefaultAsync(x => x.Id == workOrderId, token) ?? throw new NotFoundException(Messages.WorkOrderNotFound);
            if (!St.Editable.Contains(o.Status)) throw new ConflictException(Messages.WorkOrderNotEditable);
            var source = await _db.Set<WorkOrderPartIssue>().FirstOrDefaultAsync(i => i.Id == r.IssueId && i.WorkOrderId == o.Id && i.Kind == PartIssueKinds.Issue, token)
                ?? throw new NotFoundException(Messages.PartIssueNotFound);
            var quantity = decimal.Round(r.Quantity, 4);
            if (quantity <= 0) throw new ValidationFailedException(Messages.WorkOrderLineQuantityInvalid);
            var returned = await _db.Set<WorkOrderPartIssue>().Where(i => i.ReturnOfId == source.Id).SumAsync(i => (decimal?)i.Quantity, token) ?? 0;
            if (quantity > source.Quantity - returned + 0.0001m)
                throw new ValidationFailedException(string.Format(Messages.PartReturnExceeds, source.Quantity - returned));
            var date = r.Date?.Date ?? SaudiTime.Today;
            if (date > SaudiTime.Today || date < source.Date) throw new ValidationFailedException(Messages.PartReturnDateInvalid);
            var line = await _db.Set<WorkOrderLine>().AsNoTracking().FirstAsync(l => l.Id == source.LineId, token);

            var back = new WorkOrderPartIssue
            {
                WorkOrderId = o.Id, LineId = source.LineId, ProductId = source.ProductId, WarehouseId = source.WarehouseId, Kind = PartIssueKinds.Return,
                ReturnOfId = source.Id, Quantity = quantity, UnitCost = source.UnitCost, Date = date, Notes = Trim(r.Notes, 500), CreatedBy = By,
            };
            _db.Add(back);
            var note = $"إرجاع قطعة من أمر العمل {o.OrderNumber}: {line.Description}";
            var batches = ItemBatches.Take(await ItemBatches.OfDocumentAsync(_db, SourceType, o.Id, source.ProductId, source.Id, token), quantity);
            var moved = await _inventory.RecordMovementAsync(new RecordStockMovementDto
            {
                ItemId = source.ProductId, WarehouseId = source.WarehouseId, Quantity = quantity, Type = StockMovementType.AdjustmentIn, UnitCost = source.UnitCost,
                ReferenceNumber = o.OrderNumber, Date = date, Notes = note, SourceType = SourceType, SourceId = o.Id, SourceLineId = source.Id,
                BatchAllocations = batches.Count > 0 ? batches : null,
            }, token);
            back.StockMovementId = moved.Id; back.TotalCost = DocumentPricing.Round(moved.ValueChange);
            back.JournalEntryId = await PostPartsAsync(o, source.WarehouseId, back.TotalCost, date, note, back.Id, toWip: false, token);
            await _db.SaveChangesAsync(token);
            await LogAsync("WORK_ORDER_PART_RETURNED", o, $"{line.Description} • {quantity:0.####}", token);
            return await GetAsync(o.Id, token);
        }, ct);

    /// <summary>قيد القطعة: الصرف مدين تحت التنفيذ / دائن مخزون المستودع، والإرجاع عكسه. لا قيد في المخزون الدوري أو للمبلغ الصفري.</summary>
    private async Task<Guid?> PostPartsAsync(WorkOrder o, Guid warehouse, decimal amount, DateTime date, string note, Guid sourceId, bool toWip, CancellationToken ct)
    {
        if (amount <= 0) return null;
        var settings = await PostingAccounts.SettingsAsync(_db, ct);
        if (settings.IsPeriodic(date)) return null;
        var (wip, _) = await LaborAccountsAsync(ct);
        var inventory = await settings.InventoryAsync(_db, warehouse, ct);
        await DefaultAccounts.EnsureAsync(_db, ct, inventory);
        var lines = toWip
            ? new List<PostingLine> { new(wip, amount, 0, note), new(inventory, 0, amount, note) }
            : new List<PostingLine> { new(inventory, amount, 0, note), new(wip, 0, amount, note) };
        var posted = await _posting.PostAsync(new GenericPostingRequest
        {
            Date = date, Description = note, SourceType = toWip ? SourceType : SourceType + "_return", SourceId = sourceId, SourceNumber = o.OrderNumber,
            WarehouseId = warehouse, Lines = lines,
        }, ct);
        return posted.JournalEntryId;
    }

    // ---------- الخدمات الخارجية ----------

    public Task<WorkOrderExecutionDto> AddExternalAsync(Guid workOrderId, SaveExternalServiceDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var o = await ExecutingOrderAsync(workOrderId, token);
            var line = await _db.Set<WorkOrderLine>().FirstOrDefaultAsync(l => l.Id == r.LineId && l.WorkOrderId == o.Id, token)
                ?? throw new NotFoundException(Messages.WorkOrderLineNotFound);
            if (line.Kind != WorkOrderLineKinds.External) throw new ValidationFailedException(Messages.ExternalServiceLineInvalid);
            await WorkOrderRules.EnsureExecutableAsync(_db, o.Id, line.Id, token);
            if (!await _db.Set<Supplier>().AnyAsync(s => s.Id == r.SupplierId, token)) throw new ValidationFailedException(Messages.SupplierNotFound);
            if (r.EstimatedCost < 0) throw new ValidationFailedException(Messages.WorkshopSettingsNegative);
            var sent = r.SentAt is DateTime s ? Local(s) : SaudiTime.Now;
            _db.Add(new WorkOrderExternalService
            {
                WorkOrderId = o.Id, LineId = line.Id, SupplierId = r.SupplierId, Description = Trim(r.Description, 500) ?? line.Description,
                EstimatedCost = decimal.Round(r.EstimatedCost, 2), Status = XSt.Requested, SentAt = sent, Notes = Trim(r.Notes, 500), CreatedBy = By,
            });
            if (line.ExecutionStatus == LineExecutionStatuses.NotStarted) line.ExecutionStatus = LineExecutionStatuses.InProgress;
            await _db.SaveChangesAsync(token);
            await LogAsync("WORK_ORDER_EXTERNAL_REQUESTED", o, line.Description, token);
            return await GetAsync(o.Id, token);
        }, ct);

    public async Task<WorkOrderExecutionDto> CompleteExternalAsync(Guid workOrderId, Guid serviceId, CompleteExternalServiceDto r, CancellationToken ct = default)
    {
        var o = await _db.Set<WorkOrder>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == workOrderId, ct) ?? throw new NotFoundException(Messages.WorkOrderNotFound);
        if (!St.Editable.Contains(o.Status)) throw new ConflictException(Messages.WorkOrderNotEditable);
        var x = await _db.Set<WorkOrderExternalService>().FirstOrDefaultAsync(s => s.Id == serviceId && s.WorkOrderId == o.Id, ct)
            ?? throw new NotFoundException(Messages.ExternalServiceNotFound);
        if (x.Status != XSt.Requested) throw new ConflictException(Messages.ExternalServiceNotRequested);
        if (r.ActualCost < 0) throw new ValidationFailedException(Messages.WorkshopSettingsNegative);
        if (r.PurchaseInvoiceId is Guid inv)
        {
            var invoice = await _db.Set<PurchaseInvoice>().AsNoTracking().Where(i => i.Id == inv && i.SupplierId == x.SupplierId && i.Status == "posted")
                .Select(i => new { Net = i.GrandTotal - i.VatTotal }).FirstOrDefaultAsync(ct)
                ?? throw new ValidationFailedException(Messages.ExternalServiceInvoiceInvalid);
            // التسوية عند فوترة الأمر تنقل هذا المبلغ من مصروف الخدمات المشتراة: لا يتجاوز صافي الفاتورة ناقص ما رُبط بها من خدمات أخرى
            var linked = await _db.Set<WorkOrderExternalService>().Where(s => s.PurchaseInvoiceId == inv && s.Id != x.Id && s.Status == XSt.Completed)
                .SumAsync(s => (decimal?)s.ActualCost, ct) ?? 0;
            if (decimal.Round(r.ActualCost, 2) + linked > invoice.Net + 0.005m)
                throw new ValidationFailedException(string.Format(Messages.ExternalServiceExceedsInvoice, Math.Max(0, invoice.Net - linked)));
        }
        var now = SaudiTime.Now;
        var at = r.CompletedAt is DateTime c ? Local(c) : now;
        if (at > now + FutureTolerance || (x.SentAt is DateTime sent && at < sent)) throw new ValidationFailedException(Messages.TimeLogInFuture);
        x.Status = XSt.Completed; x.ActualCost = decimal.Round(r.ActualCost, 2); x.CompletedAt = at; x.PurchaseInvoiceId = r.PurchaseInvoiceId;
        await _db.SaveChangesAsync(ct);
        await LogAsync("WORK_ORDER_EXTERNAL_COMPLETED", o, $"{x.Description} • {x.ActualCost:0.##}", ct);
        return await GetAsync(o.Id, ct);
    }

    public async Task<WorkOrderExecutionDto> CancelExternalAsync(Guid workOrderId, Guid serviceId, WorkshopReasonDto r, CancellationToken ct = default)
    {
        var o = await _db.Set<WorkOrder>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == workOrderId, ct) ?? throw new NotFoundException(Messages.WorkOrderNotFound);
        if (!St.Editable.Contains(o.Status)) throw new ConflictException(Messages.WorkOrderNotEditable);
        var x = await _db.Set<WorkOrderExternalService>().FirstOrDefaultAsync(s => s.Id == serviceId && s.WorkOrderId == o.Id, ct)
            ?? throw new NotFoundException(Messages.ExternalServiceNotFound);
        if (x.Status != XSt.Requested) throw new ConflictException(Messages.ExternalServiceNotRequested);
        x.CancelReason = Trim(r.Reason, 500) ?? throw new ValidationFailedException(Messages.WorkshopCancelReasonRequired);
        x.Status = XSt.Cancelled;
        await _db.SaveChangesAsync(ct);
        await LogAsync("WORK_ORDER_EXTERNAL_CANCELLED", o, $"{x.Description} • {x.CancelReason}", ct);
        return await GetAsync(o.Id, ct);
    }

    // ---------- مساعدات ----------

    private async Task<WorkOrder> ExecutingOrderAsync(Guid id, CancellationToken ct)
    {
        var o = await _db.Set<WorkOrder>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException(Messages.WorkOrderNotFound);
        if (!ExecutionStatuses.Contains(o.Status)) throw new ConflictException(Messages.WorkOrderNotInExecution);
        return o;
    }

    private async Task<WorkOrderLine> PartLineAsync(WorkOrder o, Guid lineId, CancellationToken ct)
    {
        var line = await _db.Set<WorkOrderLine>().FirstOrDefaultAsync(l => l.Id == lineId && l.WorkOrderId == o.Id, ct)
            ?? throw new NotFoundException(Messages.WorkOrderLineNotFound);
        if (line.Kind != WorkOrderLineKinds.Part) throw new ValidationFailedException(Messages.PartLineInvalid);
        return line;
    }

    private async Task<Guid> WarehouseOfAsync(WorkOrder o, Guid? requested, CancellationToken ct)
    {
        var id = requested ?? o.WarehouseId;
        return (await WarehouseStocks.ResolveAsync(_db, id, ct)).Id;
    }

    /// <summary>يقفل صف الصنف حتى نهاية المعاملة فلا يتسابق حجزان أو صرفان على المتاح نفسه.</summary>
    private Task LockProductAsync(Guid productId, CancellationToken ct)
        => _db.Set<Product>().Where(p => p.Id == productId).ExecuteUpdateAsync(s => s.SetProperty(p => p.MinStockLevel, p => p.MinStockLevel), ct);

    /// <summary>حجوزات الصنف النشطة السارية في المستودع لأوامر أخرى (غير المستهلك منها).</summary>
    private async Task<decimal> ActiveReservedAsync(Guid productId, Guid warehouse, Guid excludeOrderId, DateTime now, CancellationToken ct)
        => (await _db.Set<WorkOrderPartReservation>().AsNoTracking()
                .Where(r => r.ProductId == productId && r.WarehouseId == warehouse && r.WorkOrderId != excludeOrderId && r.Status == RSt.Active
                            && (r.ExpiresAt == null || r.ExpiresAt > now))
                .SumAsync(r => (decimal?)(r.Quantity - r.ConsumedQuantity), ct)) ?? 0;

    private static bool IsActive(WorkOrderPartReservation r, DateTime now) => r.Status == RSt.Active && (r.ExpiresAt == null || r.ExpiresAt > now);

    private static decimal NetIssued(IEnumerable<WorkOrderPartIssue> issues, Guid lineId)
        => issues.Where(i => i.LineId == lineId).Sum(i => i.Kind == PartIssueKinds.Issue ? i.Quantity : -i.Quantity);

    /// <summary>حسابا العمالة: تحت التنفيذ والعمالة المحمّلة (يُهيَّآن قبل أول ترحيل في الوحدة).</summary>
    private async Task<(string Wip, string Absorbed)> LaborAccountsAsync(CancellationToken ct)
    {
        await ContractAccountsSetup.EnsureModuleMissingAsync(_db, ContractAccountsSetup.ModuleAccountSet.Workshop, ct);
        var s = await _db.Set<AccountingSettings>().AsNoTracking().FirstAsync(ct);
        return (s.WorkshopWipAccountCode ?? throw new ValidationFailedException(Messages.WorkshopAccountsMissing),
            s.WorkshopAbsorbedLaborAccountCode ?? throw new ValidationFailedException(Messages.WorkshopAccountsMissing));
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
