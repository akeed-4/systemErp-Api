using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using K = ERP.Core.Models.Accounting.TailoringMaterialKinds;
using L = ERP.Core.Models.Accounting.TailoringLaborStatuses;
using St = ERP.Core.Models.Accounting.TailoringOrderStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>ترحيل الخياطة المشترك: حسابا تحت التنفيذ والعمالة المحمّلة، وقيد أجر القطعة (يستدعيه الإنتاج عند خروج القطعة من مرحلة).</summary>
public static class TailoringLabor
{
    public const string LaborSource = "tailoring_labor";

    public static async Task<(string Wip, string Absorbed)> AccountsAsync(ErpDbContext db, CancellationToken ct)
    {
        await ContractAccountsSetup.EnsureModuleMissingAsync(db, ContractAccountsSetup.ModuleAccountSet.Tailoring, ct);
        var s = await db.Set<AccountingSettings>().AsNoTracking().FirstAsync(ct);
        return (s.TailoringWipAccountCode ?? throw new ValidationFailedException(Messages.TailoringAccountsMissing),
            s.TailoringAbsorbedLaborAccountCode ?? throw new ValidationFailedException(Messages.TailoringAccountsMissing));
    }

    /// <summary>أجر الخياط في المرحلة: الخاص به إن وُجد وإلا أجر المرحلة.</summary>
    public static async Task<decimal> RateAsync(ErpDbContext db, Guid employeeId, TailoringStage stage, CancellationToken ct)
        => await db.Set<TailoringWorkerRate>().AsNoTracking().Where(r => r.EmployeeId == employeeId && r.StageId == stage.Id)
            .Select(r => (decimal?)r.Rate).FirstOrDefaultAsync(ct) ?? stage.DefaultPieceRate;

    /// <summary>يقيّد أجر القطعة عن مرحلة غادرتها لأول مرة بخياط مسند وأجر موجب؛ إعادة المرحلة (تعديل أو رجوع) لا تُدفع مرتين.</summary>
    public static async Task PostStageAsync(ErpDbContext db, IAccountingPostingService posting, TailoringGarment g, TailoringStage? stage, Guid? employeeId,
        string orderNumber, string? by, CancellationToken ct)
    {
        if (stage == null || employeeId is not Guid emp) return;
        var rate = await RateAsync(db, emp, stage, ct);
        if (rate <= 0) return;
        if (await db.Set<TailoringLaborEntry>().AnyAsync(x => x.GarmentId == g.Id && x.StageId == stage.Id && x.Status == L.Posted, ct)) return;
        var entry = new TailoringLaborEntry
        {
            OrderId = g.OrderId, GarmentId = g.Id, StageId = stage.Id, EmployeeId = emp, Amount = decimal.Round(rate, 2), Date = SaudiTime.Today, CreatedBy = by,
        };
        db.Add(entry);
        entry.JournalEntryId = await PostAsync(db, posting, entry, $"أجر قطعة {g.Code} • {stage.NameAr} • أمر {orderNumber}", orderNumber, ct);
    }

    public static async Task<Guid?> PostAsync(ErpDbContext db, IAccountingPostingService posting, TailoringLaborEntry e, string note, string orderNumber, CancellationToken ct)
    {
        if (e.Amount <= 0) return null;
        var (wip, absorbed) = await AccountsAsync(db, ct);
        var posted = await posting.PostAsync(new GenericPostingRequest
        {
            Date = e.Date, Description = note, SourceType = LaborSource, SourceId = e.Id, SourceNumber = orderNumber,
            Lines = new List<PostingLine> { new(wip, e.Amount, 0, note), new(absorbed, 0, e.Amount, note) },
        }, ct);
        return posted.JournalEntryId;
    }
}

/// <summary>
/// التكلفة الفعلية للأمر. القواعد:
/// <list type="bullet">
/// <item>الصرف لأمر قائم مؤكد غير مُسلَّم كلياً، لصنف مخزني ضمن رصيد الموقع: حركة <c>AdjustmentOut</c> بتكلفتها الفعلية، وقيد مدين تحت
/// التنفيذ / دائن مخزون الموقع (لا قيد في المخزون الدوري). الإرجاع على صرف بعينه ضمن الباقي منه وبتكلفته، وقيده العكس.</item>
/// <item>أجر القطعة آلي من الإنتاج (<see cref="TailoringLabor.PostStageAsync"/>) أو يدوي بصلاحية تعديل الخياطين؛ الإلغاء بسبب وقيد عكسي
/// قبل إغلاق الأمر.</item>
/// <item>الربحية = صافي الأمر قبل الضريبة − (صافي المواد + الأجور السارية)، وتتطلب صلاحية رؤية التكلفة.</item>
/// </list>
/// </summary>
public class TailoringCostingService : ITailoringCostingService
{
    public const string MaterialSource = "tailoring_material";
    private static readonly string[] Issuable = St.Open.Where(s => s != St.Draft).ToArray();

    private readonly ErpDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly IAccountingPostingService _posting;
    private readonly ITransactionRunner _tx;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;
    private readonly IAuditService _audit;

    public TailoringCostingService(ErpDbContext db, IInventoryService inventory, IAccountingPostingService posting, ITransactionRunner tx, ICurrentUser user,
        IPermissionService permissions, IAuditService audit)
    {
        _db = db; _inventory = inventory; _posting = posting; _tx = tx; _user = user; _permissions = permissions; _audit = audit;
    }

    private string? By => _user.Name ?? _user.Email;
    private Task<bool> CanSeeCostAsync(CancellationToken ct) => _permissions.HasPermissionAsync(PermissionCatalog.TailoringCostViewScreenId, ScreenAction.View, ct);

    // ---------- المواد ----------

    public async Task<List<TailoringMaterialDto>> MaterialsAsync(Guid? orderId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var q = _db.Set<TailoringMaterialIssue>().AsNoTracking();
        if (orderId is Guid oid) q = q.Where(i => i.OrderId == oid);
        if (from is DateTime f) q = q.Where(i => i.Date >= f.Date);
        if (to is DateTime t) q = q.Where(i => i.Date <= t.Date);
        var rows = await q.OrderByDescending(i => i.Date).ThenByDescending(i => i.CreatedAt).Take(orderId == null ? 500 : 2000).ToListAsync(ct);
        return await MapMaterialsAsync(rows, await CanSeeCostAsync(ct), ct);
    }

    public Task<TailoringMaterialDto> IssueAsync(IssueTailoringMaterialDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var o = await _db.Set<TailoringOrder>().FirstOrDefaultAsync(x => x.Id == r.OrderId, token) ?? throw new NotFoundException(Messages.TailoringOrderNotFound);
            if (!Issuable.Contains(o.Status)) throw new ConflictException(Messages.TailoringOrderNotConfirmed);
            if (r.GarmentId is Guid gid && !await _db.Set<TailoringGarment>().AnyAsync(g => g.Id == gid && g.OrderId == o.Id, token))
                throw new NotFoundException(Messages.TailoringGarmentNotFound);
            var product = await _db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == r.ProductId, token)
                ?? throw new NotFoundException(Messages.ItemNotFoundDot);
            if (product.ItemType != ProductItemTypes.Stock) throw new ValidationFailedException(Messages.TailoringMaterialNotStock);
            var quantity = decimal.Round(r.Quantity, 4);
            if (quantity <= 0) throw new ValidationFailedException(Messages.TailoringMaterialQuantityInvalid);
            var date = r.Date?.Date ?? SaudiTime.Today;
            if (date > SaudiTime.Today) throw new ValidationFailedException(Messages.TailoringMaterialDateInvalid);
            var defaultWarehouse = await _db.Set<TailoringSettings>().AsNoTracking().Select(s => s.DefaultWarehouseId).FirstOrDefaultAsync(token);
            var warehouse = (await WarehouseStocks.ResolveAsync(_db, r.WarehouseId ?? o.WarehouseId ?? defaultWarehouse, token)).Id;
            await _db.Set<Product>().Where(p => p.Id == product.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.MinStockLevel, p => p.MinStockLevel), token);
            var available = await WarehouseStocks.QuantityAsync(_db, product.Id, warehouse, token);
            if (quantity > available + 0.0001m) throw new ConflictException(string.Format(Messages.TailoringMaterialNotAvailable, product.NameAr, Math.Max(0, available)));

            var issue = new TailoringMaterialIssue
            {
                OrderId = o.Id, GarmentId = r.GarmentId, ProductId = product.Id, WarehouseId = warehouse, Kind = K.Issue, Quantity = quantity, Date = date,
                Notes = Trim(r.Notes, 500), CreatedBy = By,
            };
            _db.Add(issue);
            var note = $"صرف {product.NameAr} لأمر التفصيل {o.OrderNumber}";
            var moved = await _inventory.RecordMovementAsync(new RecordStockMovementDto
            {
                ItemId = product.Id, WarehouseId = warehouse, Quantity = quantity, Type = StockMovementType.AdjustmentOut, ReferenceNumber = o.OrderNumber,
                Date = date, Notes = note, SourceType = MaterialSource, SourceId = o.Id, SourceLineId = issue.Id,
            }, token);
            issue.StockMovementId = moved.Id; issue.UnitCost = moved.UnitCost; issue.TotalCost = DocumentPricing.Round(-moved.ValueChange);
            issue.JournalEntryId = await PostMaterialAsync(o, warehouse, issue.TotalCost, date, note, issue.Id, toWip: true, token);
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("TAILORING_MATERIAL_ISSUED", nameof(TailoringOrder), o.Id.ToString(), $"{o.OrderNumber} • {product.NameAr} • {quantity:0.####}", token);
            return (await MapMaterialsAsync(new List<TailoringMaterialIssue> { issue }, await CanSeeCostAsync(token), token))[0];
        }, ct);

    public Task<TailoringMaterialDto> ReturnAsync(ReturnTailoringMaterialDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var source = await _db.Set<TailoringMaterialIssue>().FirstOrDefaultAsync(i => i.Id == r.IssueId && i.Kind == K.Issue, token)
                ?? throw new NotFoundException(Messages.TailoringMaterialNotFound);
            var o = await _db.Set<TailoringOrder>().FirstAsync(x => x.Id == source.OrderId, token);
            if (o.Status is St.Closed or St.Cancelled) throw new ConflictException(Messages.TailoringOrderNotConfirmed);
            var quantity = decimal.Round(r.Quantity, 4);
            if (quantity <= 0) throw new ValidationFailedException(Messages.TailoringMaterialQuantityInvalid);
            var returned = await _db.Set<TailoringMaterialIssue>().Where(i => i.ReturnOfId == source.Id).SumAsync(i => (decimal?)i.Quantity, token) ?? 0;
            if (quantity > source.Quantity - returned + 0.0001m)
                throw new ValidationFailedException(string.Format(Messages.TailoringMaterialReturnExceeds, source.Quantity - returned));
            var date = r.Date?.Date ?? SaudiTime.Today;
            if (date > SaudiTime.Today || date < source.Date) throw new ValidationFailedException(Messages.TailoringMaterialDateInvalid);
            var name = await _db.Set<Product>().Where(p => p.Id == source.ProductId).Select(p => p.NameAr).FirstAsync(token);

            var back = new TailoringMaterialIssue
            {
                OrderId = o.Id, GarmentId = source.GarmentId, ProductId = source.ProductId, WarehouseId = source.WarehouseId, Kind = K.Return, ReturnOfId = source.Id,
                Quantity = quantity, UnitCost = source.UnitCost, Date = date, Notes = Trim(r.Notes, 500), CreatedBy = By,
            };
            _db.Add(back);
            var note = $"إرجاع {name} من أمر التفصيل {o.OrderNumber}";
            var batches = ItemBatches.Take(await ItemBatches.OfDocumentAsync(_db, MaterialSource, o.Id, source.ProductId, source.Id, token), quantity);
            var moved = await _inventory.RecordMovementAsync(new RecordStockMovementDto
            {
                ItemId = source.ProductId, WarehouseId = source.WarehouseId, Quantity = quantity, Type = StockMovementType.AdjustmentIn, UnitCost = source.UnitCost,
                ReferenceNumber = o.OrderNumber, Date = date, Notes = note, SourceType = MaterialSource, SourceId = o.Id, SourceLineId = source.Id,
                BatchAllocations = batches.Count > 0 ? batches : null,
            }, token);
            back.StockMovementId = moved.Id; back.TotalCost = DocumentPricing.Round(moved.ValueChange);
            back.JournalEntryId = await PostMaterialAsync(o, source.WarehouseId, back.TotalCost, date, note, back.Id, toWip: false, token);
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("TAILORING_MATERIAL_RETURNED", nameof(TailoringOrder), o.Id.ToString(), $"{o.OrderNumber} • {name} • {quantity:0.####}", token);
            return (await MapMaterialsAsync(new List<TailoringMaterialIssue> { back }, await CanSeeCostAsync(token), token))[0];
        }, ct);

    /// <summary>الصرف مدين تحت التنفيذ / دائن مخزون الموقع، والإرجاع عكسه. لا قيد في المخزون الدوري أو للمبلغ الصفري.</summary>
    private async Task<Guid?> PostMaterialAsync(TailoringOrder o, Guid warehouse, decimal amount, DateTime date, string note, Guid sourceId, bool toWip, CancellationToken ct)
    {
        if (amount <= 0) return null;
        var settings = await PostingAccounts.SettingsAsync(_db, ct);
        if (settings.IsPeriodic(date)) return null;
        var (wip, _) = await TailoringLabor.AccountsAsync(_db, ct);
        var inventory = await settings.InventoryAsync(_db, warehouse, ct);
        await DefaultAccounts.EnsureAsync(_db, ct, inventory);
        var lines = toWip
            ? new List<PostingLine> { new(wip, amount, 0, note), new(inventory, 0, amount, note) }
            : new List<PostingLine> { new(inventory, amount, 0, note), new(wip, 0, amount, note) };
        var posted = await _posting.PostAsync(new GenericPostingRequest
        {
            Date = date, Description = note, SourceType = toWip ? MaterialSource : MaterialSource + "_return", SourceId = sourceId, SourceNumber = o.OrderNumber,
            WarehouseId = warehouse, Lines = lines,
        }, ct);
        return posted.JournalEntryId;
    }

    private async Task<List<TailoringMaterialDto>> MapMaterialsAsync(List<TailoringMaterialIssue> rows, bool cost, CancellationToken ct)
    {
        var orderIds = rows.Select(r => r.OrderId).Distinct().ToList();
        var productIds = rows.Select(r => r.ProductId).Distinct().ToList();
        var garmentIds = rows.Where(r => r.GarmentId != null).Select(r => r.GarmentId!.Value).Distinct().ToList();
        var warehouseIds = rows.Select(r => r.WarehouseId).Distinct().ToList();
        var issueIds = rows.Where(r => r.Kind == K.Issue).Select(r => r.Id).ToList();
        var orders = await _db.Set<TailoringOrder>().AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.OrderNumber, ct);
        var products = await _db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => new { p.NameAr, p.Unit }, ct);
        var garments = await _db.Set<TailoringGarment>().AsNoTracking().Where(g => garmentIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Code, ct);
        var warehouses = await _db.Set<Warehouse>().AsNoTracking().Where(w => warehouseIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.NameAr, ct);
        var returned = await _db.Set<TailoringMaterialIssue>().AsNoTracking().Where(i => i.ReturnOfId != null && issueIds.Contains(i.ReturnOfId.Value))
            .GroupBy(i => i.ReturnOfId!.Value).Select(g => new { g.Key, Qty = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        return rows.Select(i => new TailoringMaterialDto
        {
            Id = i.Id, OrderId = i.OrderId, OrderNumber = orders.GetValueOrDefault(i.OrderId) ?? string.Empty, GarmentId = i.GarmentId,
            GarmentCode = i.GarmentId is Guid g ? garments.GetValueOrDefault(g) : null, ProductId = i.ProductId,
            ProductName = products.GetValueOrDefault(i.ProductId)?.NameAr ?? string.Empty, Unit = products.GetValueOrDefault(i.ProductId)?.Unit,
            WarehouseId = i.WarehouseId, WarehouseName = warehouses.GetValueOrDefault(i.WarehouseId), Kind = i.Kind, ReturnOfId = i.ReturnOfId, Quantity = i.Quantity,
            ReturnableQuantity = i.Kind == K.Issue ? i.Quantity - returned.GetValueOrDefault(i.Id) : 0,
            UnitCost = cost ? i.UnitCost : null, TotalCost = cost ? i.TotalCost : null, Date = i.Date, Notes = i.Notes, CreatedBy = i.CreatedBy,
        }).ToList();
    }

    // ---------- الأجور ----------

    public async Task<List<TailoringLaborDto>> LaborAsync(Guid? orderId, Guid? employeeId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var q = _db.Set<TailoringLaborEntry>().AsNoTracking();
        if (orderId is Guid oid) q = q.Where(x => x.OrderId == oid);
        if (employeeId is Guid eid) q = q.Where(x => x.EmployeeId == eid);
        if (from is DateTime f) q = q.Where(x => x.Date >= f.Date);
        if (to is DateTime t) q = q.Where(x => x.Date <= t.Date);
        return await MapLaborAsync(await q.OrderByDescending(x => x.Date).ThenByDescending(x => x.CreatedAt).Take(2000).ToListAsync(ct), ct);
    }

    public Task<TailoringLaborDto> AddLaborAsync(SaveTailoringLaborDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var g = await _db.Set<TailoringGarment>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.GarmentId, token)
                ?? throw new NotFoundException(Messages.TailoringGarmentNotFound);
            var o = await _db.Set<TailoringOrder>().AsNoTracking().FirstAsync(x => x.Id == g.OrderId, token);
            if (o.Status is St.Draft or St.Closed or St.Cancelled) throw new ConflictException(Messages.TailoringOrderNotConfirmed);
            if (!await _db.Set<Employee>().AnyAsync(e => e.Id == r.EmployeeId && e.Status == EmployeeStatuses.Active, token))
                throw new ValidationFailedException(Messages.TailoringEmployeeNotFound);
            if (r.StageId is Guid sid && !await _db.Set<TailoringStage>().AnyAsync(s => s.Id == sid, token))
                throw new ValidationFailedException(Messages.TailoringStageNotApplicable);
            var amount = decimal.Round(r.Amount, 2);
            if (amount <= 0) throw new ValidationFailedException(Messages.TailoringLaborAmountInvalid);
            var date = r.Date?.Date ?? SaudiTime.Today;
            if (date > SaudiTime.Today) throw new ValidationFailedException(Messages.TailoringMaterialDateInvalid);
            var e = new TailoringLaborEntry
            {
                OrderId = o.Id, GarmentId = g.Id, StageId = r.StageId, EmployeeId = r.EmployeeId, Amount = amount, Date = date, IsManual = true,
                Notes = Trim(r.Notes, 500), CreatedBy = By,
            };
            _db.Add(e);
            e.JournalEntryId = await TailoringLabor.PostAsync(_db, _posting, e, $"أجر قطعة {g.Code} • أمر {o.OrderNumber}", o.OrderNumber, token);
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("TAILORING_LABOR_ADDED", nameof(TailoringOrder), o.Id.ToString(), $"{o.OrderNumber} • {g.Code} • {amount:0.##}", token);
            return (await MapLaborAsync(new List<TailoringLaborEntry> { e }, token))[0];
        }, ct);

    public Task<TailoringLaborDto> CancelLaborAsync(Guid id, string? reason, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var e = await _db.Set<TailoringLaborEntry>().FirstOrDefaultAsync(x => x.Id == id, token) ?? throw new NotFoundException(Messages.TailoringLaborNotFound);
            if (e.Status != L.Posted) throw new ConflictException(Messages.TailoringLaborNotFound);
            var text = Trim(reason, 500) ?? throw new ValidationFailedException(Messages.TailoringReasonRequired);
            var o = await _db.Set<TailoringOrder>().AsNoTracking().FirstAsync(x => x.Id == e.OrderId, token);
            if (o.Status == St.Closed) throw new ConflictException(Messages.TailoringOrderNotConfirmed);
            if (e.JournalEntryId is Guid je) e.ReversalEntryId = (await _posting.ReverseAsync(je, text, token)).JournalEntryId;
            e.Status = L.Cancelled; e.CancelReason = text;
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("TAILORING_LABOR_CANCELLED", nameof(TailoringOrder), o.Id.ToString(), $"{o.OrderNumber} • {e.Amount:0.##} • {text}", token);
            return (await MapLaborAsync(new List<TailoringLaborEntry> { e }, token))[0];
        }, ct);

    private async Task<List<TailoringLaborDto>> MapLaborAsync(List<TailoringLaborEntry> rows, CancellationToken ct)
    {
        var orderIds = rows.Select(r => r.OrderId).Distinct().ToList();
        var garmentIds = rows.Select(r => r.GarmentId).Distinct().ToList();
        var empIds = rows.Select(r => r.EmployeeId).Distinct().ToList();
        var orders = await _db.Set<TailoringOrder>().AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.OrderNumber, ct);
        var garments = await _db.Set<TailoringGarment>().AsNoTracking().Where(g => garmentIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Code, ct);
        var emps = await _db.Set<Employee>().AsNoTracking().Where(e => empIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.NameAr, ct);
        var stages = await _db.Set<TailoringStage>().AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.NameAr, ct);
        return rows.Select(e => new TailoringLaborDto
        {
            Id = e.Id, OrderId = e.OrderId, OrderNumber = orders.GetValueOrDefault(e.OrderId) ?? string.Empty, GarmentId = e.GarmentId,
            GarmentCode = garments.GetValueOrDefault(e.GarmentId) ?? string.Empty, StageId = e.StageId, StageName = e.StageId is Guid s ? stages.GetValueOrDefault(s) : null,
            EmployeeId = e.EmployeeId, EmployeeName = emps.GetValueOrDefault(e.EmployeeId) ?? string.Empty, Amount = e.Amount, Date = e.Date, IsManual = e.IsManual,
            Status = e.Status, Notes = e.Notes, CancelReason = e.CancelReason,
        }).ToList();
    }

    // ---------- الخياطون ----------

    public async Task<List<TailoringWorkerSummaryDto>> WorkersAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var q = _db.Set<TailoringLaborEntry>().AsNoTracking().Where(x => x.Status == L.Posted);
        if (from is DateTime f) q = q.Where(x => x.Date >= f.Date);
        if (to is DateTime t) q = q.Where(x => x.Date <= t.Date);
        var earned = await q.GroupBy(x => x.EmployeeId)
            .Select(g => new { g.Key, Pieces = g.Select(x => x.GarmentId).Distinct().Count(), Amount = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Key, ct);
        var open = await (from g in _db.Set<TailoringGarment>().AsNoTracking()
                          join o in _db.Set<TailoringOrder>().AsNoTracking() on g.OrderId equals o.Id
                          where St.Open.Contains(o.Status) && g.AssignedEmployeeId != null
                                && g.Status != TailoringGarmentStatuses.Ready && g.Status != TailoringGarmentStatuses.Delivered && g.Status != TailoringGarmentStatuses.Cancelled
                          group g by g.AssignedEmployeeId!.Value into grp
                          select new { grp.Key, Count = grp.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var rates = (await _db.Set<TailoringWorkerRate>().AsNoTracking().ToListAsync(ct)).ToLookup(r => r.EmployeeId);
        var cost = await CanSeeCostAsync(ct);
        return (await _db.Set<Employee>().AsNoTracking().Where(e => e.Status == EmployeeStatuses.Active).OrderBy(e => e.NameAr)
                .Select(e => new { e.Id, e.Code, e.NameAr }).ToListAsync(ct))
            .Select(e => new TailoringWorkerSummaryDto
            {
                EmployeeId = e.Id, Code = e.Code, Name = e.NameAr, Pieces = earned.GetValueOrDefault(e.Id)?.Pieces ?? 0,
                Amount = cost ? earned.GetValueOrDefault(e.Id)?.Amount ?? 0 : 0, OpenPieces = open.GetValueOrDefault(e.Id),
                Rates = rates[e.Id].Select(r => new TailoringWorkerRateDto { StageId = r.StageId, Rate = r.Rate }).ToList(),
            }).ToList();
    }

    public Task SaveWorkerRatesAsync(Guid employeeId, SaveTailoringWorkerRatesDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            if (!await _db.Set<Employee>().AnyAsync(e => e.Id == employeeId, token)) throw new ValidationFailedException(Messages.TailoringEmployeeNotFound);
            var stageIds = await _db.Set<TailoringStage>().Select(s => s.Id).ToListAsync(token);
            var wanted = r.Rates.Where(x => x.Rate > 0).GroupBy(x => x.StageId).Select(g => g.Last()).ToList();
            if (wanted.Any(x => !stageIds.Contains(x.StageId))) throw new ValidationFailedException(Messages.TailoringStageNotApplicable);
            var existing = await _db.Set<TailoringWorkerRate>().Where(x => x.EmployeeId == employeeId).ToListAsync(token);
            _db.RemoveRange(existing.Where(x => wanted.All(w => w.StageId != x.StageId)));
            foreach (var w in wanted)
            {
                var row = existing.FirstOrDefault(x => x.StageId == w.StageId);
                if (row == null) _db.Add(new TailoringWorkerRate { EmployeeId = employeeId, StageId = w.StageId, Rate = decimal.Round(w.Rate, 2) });
                else row.Rate = decimal.Round(w.Rate, 2);
            }
            await _db.SaveChangesAsync(token);
        }, ct);

    // ---------- الربحية ----------

    public async Task<TailoringProfitabilityDto> ProfitabilityAsync(Guid orderId, CancellationToken ct = default)
    {
        await _permissions.DemandAsync(PermissionCatalog.TailoringCostViewScreenId, ScreenAction.View, null, ct);
        var o = await _db.Set<TailoringOrder>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == orderId, ct) ?? throw new NotFoundException(Messages.TailoringOrderNotFound);
        var materials = await MaterialsAsync(o.Id, null, null, ct);
        var labor = await LaborAsync(o.Id, null, null, null, ct);
        var materialsCost = materials.Sum(m => (m.Kind == K.Issue ? 1 : -1) * (m.TotalCost ?? 0));
        var laborCost = labor.Where(l => l.Status == L.Posted).Sum(l => l.Amount);
        var total = materialsCost + laborCost;
        var profit = o.NetTotal - total;
        return new TailoringProfitabilityDto
        {
            OrderId = o.Id, OrderNumber = o.OrderNumber, Revenue = o.NetTotal, MaterialsCost = materialsCost, LaborCost = laborCost, TotalCost = total, Profit = profit,
            MarginPercent = o.NetTotal > 0 ? decimal.Round(profit / o.NetTotal * 100m, 2) : 0, Materials = materials, Labor = labor,
        };
    }

    private static string? Trim(string? value, int max)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length > max ? v[..max] : v;
    }
}
