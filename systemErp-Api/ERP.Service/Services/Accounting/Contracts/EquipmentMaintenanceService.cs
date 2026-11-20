using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using ESt = ERP.Core.Models.Accounting.EquipmentStatuses;
using St = ERP.Core.Models.Accounting.EquipmentMaintenanceStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// أوامر عمل صيانة المعدات. القواعد:
/// <list type="bullet">
/// <item>«بدء التنفيذ» يضع المعدة في الصيانة (لا تُسند ولا تُحوَّل)، والإكمال أو الإلغاء يعيدها لحالتها السابقة.</item>
/// <item>الإكمال يصرف قطع الغيار من مستودع الأمر بتكلفتها الفعلية (حركة مخزون) ويقيد مدين مصروف الصيانة / دائن المخزون. تكلفة العمالة
/// والورشة الخارجية تُقيَّد فقط إن حُدِّد حسابها الدائن؛ وإلا تبقى للمتابعة (مقيَّدة من فاتورة الورشة أو الرواتب) فلا تتكرر.</item>
/// <item>الصيانة الوقائية المكتملة تحدّث تاريخ آخر صيانة فتُحسب المستحقة التالية منه.</item>
/// <item>إلغاء أمر مكتمل (بسبب) يعيد القطع للمستودع بتكلفة صرفها ويعكس القيد.</item>
/// </list>
/// </summary>
public class EquipmentMaintenanceService : IEquipmentMaintenanceService
{
    internal const string SourceType = "equipment_maintenance";
    private readonly ErpDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly IAccountingPostingService _posting;
    private readonly INumberSequenceService _numbers;
    private readonly ITransactionRunner _tx;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;

    public EquipmentMaintenanceService(ErpDbContext db, IInventoryService inventory, IAccountingPostingService posting, INumberSequenceService numbers,
        ITransactionRunner tx, ICurrentUser user, IAuditService audit)
    {
        _db = db; _inventory = inventory; _posting = posting; _numbers = numbers; _tx = tx; _user = user; _audit = audit;
    }

    public async Task<List<EquipmentMaintenanceOrderDto>> ListAsync(Guid? equipmentId, string? status, CancellationToken ct = default)
    {
        var q = _db.Set<EquipmentMaintenanceOrder>().AsNoTracking().Include(o => o.Parts).AsQueryable();
        if (equipmentId is Guid eq) q = q.Where(o => o.EquipmentId == eq);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(o => o.Status == status);
        return await ToDtosAsync(await q.OrderByDescending(o => o.ScheduledDate).ThenByDescending(o => o.CreatedAt).Take(2000).ToListAsync(ct), withStock: false, ct);
    }

    public async Task<EquipmentMaintenanceOrderDto> GetAsync(Guid id, CancellationToken ct = default)
        => (await ToDtosAsync(new List<EquipmentMaintenanceOrder> { await LoadAsync(id, track: false, ct) }, withStock: true, ct)).Single();

    public Task<EquipmentMaintenanceOrderDto> CreateAsync(SaveEquipmentMaintenanceOrderDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var date = request.ScheduledDate?.Date ?? SaudiTime.Today;
            var o = new EquipmentMaintenanceOrder { Status = St.Open };
            await ApplyAsync(o, request, date, token);
            o.OrderNumber = await _numbers.AssignAsync(new DocumentNumberRequest
            {
                Key = SourceType, DefaultPrefix = "EMO-", WarehouseId = o.WarehouseId, Date = date,
                Exists = (n, c) => _db.Set<EquipmentMaintenanceOrder>().AnyAsync(x => x.OrderNumber == n, c),
            }, token);
            _db.Add(o);
            await _db.SaveChangesAsync(token);
            return await GetAsync(o.Id, token);
        }, ct);

    public Task<EquipmentMaintenanceOrderDto> UpdateAsync(Guid id, SaveEquipmentMaintenanceOrderDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var o = await LoadAsync(id, track: true, token);
            if (o.Status is not (St.Open or St.InProgress)) throw new ConflictException($"أمر العمل {o.OrderNumber} مكتمل أو ملغى؛ لا يُعدَّل.");
            if (request.EquipmentId != o.EquipmentId) throw new ValidationFailedException("لا تُغيَّر معدة أمر العمل؛ ألغِه وأنشئ أمراً جديداً.");
            foreach (var old in o.Parts.ToList())
            {
                o.Parts.Remove(old);
                _db.Remove(old);
            }
            await ApplyAsync(o, request, request.ScheduledDate?.Date ?? o.ScheduledDate, token);
            await _db.SaveChangesAsync(token);
            return await GetAsync(id, token);
        }, ct);

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var o = await LoadAsync(id, track: true, ct);
        if (o.Status != St.Open) throw new ConflictException($"أمر العمل {o.OrderNumber} بدأ تنفيذه؛ ألغِه بدل حذفه.");
        _db.RemoveRange(o.Parts);
        _db.Remove(o);
        await _db.SaveChangesAsync(ct);
    }

    public Task<EquipmentMaintenanceOrderDto> StartAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var o = await LoadAsync(id, track: true, token);
            if (o.Status != St.Open) throw new ConflictException($"أمر العمل {o.OrderNumber} ليس مفتوحاً.");
            var e = await EquipmentService.FindAsync(_db, o.EquipmentId, track: true, token);
            o.Status = St.InProgress; o.StartedAt = SaudiTime.Now;
            if (e.Status != ESt.Maintenance) { o.StatusBefore = e.Status; e.Status = ESt.Maintenance; }
            await _db.SaveChangesAsync(token);
            return await GetAsync(id, token);
        }, ct);

    public Task<EquipmentMaintenanceOrderDto> CompleteAsync(Guid id, CompleteEquipmentMaintenanceDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var o = await LoadAsync(id, track: true, token);
            if (o.Status is not (St.Open or St.InProgress)) throw new ConflictException($"أمر العمل {o.OrderNumber} مكتمل أو ملغى.");
            var e = await EquipmentService.FindAsync(_db, o.EquipmentId, track: true, token);
            var date = request.CompletedAt?.Date ?? SaudiTime.Today;
            if (date > SaudiTime.Today) throw new ValidationFailedException("تاريخ الإكمال لا يكون في المستقبل.");
            if (request.MeterReading < 0 || request.DowntimeHours < 0) throw new ValidationFailedException("القيم لا تكون سالبة.");
            if (!string.IsNullOrWhiteSpace(request.WorkDone)) o.WorkDone = EquipmentService.Trim(request.WorkDone, 2000);
            if (request.MeterReading is decimal reading) o.MeterReading = reading;
            if (request.DowntimeHours is decimal downtime) o.DowntimeHours = downtime;
            var note = $"صيانة {(o.Type == EquipmentMaintenanceTypes.Preventive ? "وقائية" : "تصحيحية")} {o.OrderNumber} - المعدة {e.Code} {e.NameAr}";

            // قطع الغيار: صادر من مستودع الأمر بتكلفته الفعلية
            decimal parts = 0;
            if (o.Parts.Count > 0)
            {
                var warehouseId = o.WarehouseId ?? throw new ValidationFailedException("حدّد مستودع قطع الغيار قبل الإكمال.");
                foreach (var g in o.Parts.GroupBy(p => p.ProductId))
                {
                    var stock = await WarehouseStocks.QuantityAsync(_db, g.Key, warehouseId, token);
                    var needed = g.Sum(p => p.Quantity);
                    if (needed > stock + 0.0001m)
                        throw new ValidationFailedException($"«{g.First().ItemName}»: المطلوب {needed:0.####} يتجاوز رصيد المستودع {Math.Max(0, stock):0.####}.");
                }
                foreach (var p in o.Parts.OrderBy(p => p.SortOrder))
                {
                    var moved = await _inventory.RecordMovementAsync(new RecordStockMovementDto
                    {
                        ItemId = p.ProductId, WarehouseId = warehouseId, Quantity = p.Quantity, Type = StockMovementType.AdjustmentOut,
                        ReferenceNumber = o.OrderNumber, Date = date, Notes = note, SourceType = SourceType, SourceId = o.Id, SourceLineId = p.Id,
                    }, token);
                    p.UnitCost = moved.UnitCost;
                    p.TotalCost = -moved.ValueChange;
                    parts += p.TotalCost;
                }
            }
            o.PartsCost = DocumentPricing.Round(parts);
            o.TotalCost = o.PartsCost + o.LaborCost + o.ExternalCost;

            // القيد: مصروف الصيانة مقابل المخزون (القطع) وحساب العمالة/الورشة إن حُدِّد
            var settings = await PostingAccounts.SettingsAsync(_db, token);
            var extra = new List<PostingLine>();
            if (o.PartsCost > 0 && !settings.IsPeriodic(date))
            {
                var inventory = await settings.InventoryAsync(_db, o.WarehouseId, token);
                await DefaultAccounts.EnsureAsync(_db, token, inventory);
                extra.Add(new PostingLine(inventory, 0, o.PartsCost, note));
            }
            var services = o.LaborCost + o.ExternalCost;
            var creditServices = services > 0 && !string.IsNullOrWhiteSpace(o.CreditAccountCode);
            if (extra.Count > 0 || creditServices)
            {
                var posted = await EquipmentService.PostCostAsync(_db, _posting, new EquipmentService.CostPosting(e, o.ChargeToProject ? o.ContractId : null,
                    o.ExpenseAccountCode, creditServices ? o.CreditAccountCode : null, creditServices ? services : 0, date, SourceType, o.Id, o.OrderNumber, note, extra), token);
                o.JournalEntryId = posted.JournalEntryId; o.ExpenseAccountCode = posted.ExpenseAccount; o.CostCenterId = posted.CostCenterId;
                o.ChargeToProject = posted.ChargedToProject;
            }
            else o.ChargeToProject = false;

            o.Status = St.Completed; o.CompletedAt = date; o.CompletedBy = _user.Name;
            if (o.MeterReading is decimal meter) e.MeterHours = Math.Max(e.MeterHours, meter);
            if (o.Type == EquipmentMaintenanceTypes.Preventive)
            {
                e.LastServiceDate = date;
                e.LastServiceMeter = o.MeterReading ?? e.MeterHours;
            }
            await RestoreStatusAsync(o, e, token);
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("EQUIPMENT_MAINTENANCE_COMPLETED", nameof(EquipmentMaintenanceOrder), o.Id.ToString(), $"{note} بتكلفة {o.TotalCost:0.00}", token);
            return await GetAsync(id, token);
        }, ct);

    public Task<EquipmentMaintenanceOrderDto> CancelAsync(Guid id, string? reason, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var o = await LoadAsync(id, track: true, token);
            if (o.Status == St.Cancelled) throw new ConflictException($"أمر العمل {o.OrderNumber} ملغى من قبل.");
            var e = await EquipmentService.FindAsync(_db, o.EquipmentId, track: true, token);
            var why = EquipmentService.Trim(reason, 500);
            if (o.Status == St.Completed)
            {
                if (why == null) throw new ValidationFailedException("سبب إلغاء أمر عمل مكتمل مطلوب.");
                var note = $"إلغاء أمر الصيانة {o.OrderNumber}: {why}";
                if (o.JournalEntryId is Guid entry) await _posting.ReverseAsync(entry, note, token);
                decimal residual = 0;
                foreach (var p in o.Parts.Where(p => p.Quantity > 0))
                {
                    var back = await _inventory.RecordMovementAsync(new RecordStockMovementDto
                    {
                        ItemId = p.ProductId, WarehouseId = o.WarehouseId, Quantity = p.Quantity, Type = StockMovementType.AdjustmentIn, UnitCost = p.UnitCost,
                        ReferenceNumber = o.OrderNumber, Date = o.CompletedAt ?? SaudiTime.Today, Notes = note,
                        SourceType = SourceType + "_reversal", SourceId = o.Id, SourceLineId = p.Id,
                        BatchAllocations = await ItemBatches.OfDocumentAsync(_db, SourceType, o.Id, p.ProductId, p.Id, token),
                    }, token);
                    residual += back.ValueChange - p.TotalCost;
                }
                await ItemBatches.MarkReversedAsync(_db, SourceType, o.Id, token);
                await _db.SaveChangesAsync(token);
                if (o.JournalEntryId != null && residual != 0)
                {
                    var settings = await PostingAccounts.SettingsAsync(_db, token);
                    await InventoryValuation.PostRevaluationAsync(_db, _posting, residual, await settings.InventoryAsync(_db, o.WarehouseId, token), note, o.Id, token);
                }
                o.JournalEntryId = null;
            }
            else await RestoreStatusAsync(o, e, token);
            o.Status = St.Cancelled; o.CancelReason = why;
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("EQUIPMENT_MAINTENANCE_CANCELLED", nameof(EquipmentMaintenanceOrder), o.Id.ToString(), $"إلغاء أمر الصيانة {o.OrderNumber} • {why}", token);
            return await GetAsync(id, token);
        }, ct);

    /// <summary>المعدة التي وضعها هذا الأمر في الصيانة تعود لحالتها السابقة، ما لم يبقَ أمر آخر قيد التنفيذ عليها.</summary>
    private async Task RestoreStatusAsync(EquipmentMaintenanceOrder o, Equipment e, CancellationToken ct)
    {
        if (e.Status != ESt.Maintenance) return;
        if (await _db.Set<EquipmentMaintenanceOrder>().AnyAsync(x => x.EquipmentId == e.Id && x.Id != o.Id && x.Status == St.InProgress, ct)) return;
        var assigned = await _db.Set<EquipmentAssignment>().AnyAsync(a => a.EquipmentId == e.Id && a.Status == EquipmentAssignmentStatuses.Active, ct);
        // عند عميل بعقد تأجير (سطر خارج لم يُستلم): تعود «مؤجَّرة»
        var rented = !assigned && await _db.Set<RentalContractLine>().AnyAsync(l => l.EquipmentId == e.Id && l.Status == RentalLineStatuses.Out, ct);
        e.Status = assigned ? ESt.Assigned : rented ? ESt.Rented : o.StatusBefore is ESt.OutOfService or ESt.Returned ? o.StatusBefore : ESt.Available;
    }

    private async Task ApplyAsync(EquipmentMaintenanceOrder o, SaveEquipmentMaintenanceOrderDto r, DateTime date, CancellationToken ct)
    {
        var errors = new List<string>();
        var e = await _db.Set<Equipment>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.EquipmentId, ct);
        if (e == null) errors.Add("المعدة غير موجودة.");
        if (!EquipmentMaintenanceTypes.IsValid(r.Type)) errors.Add("نوع الصيانة غير صحيح (وقائية أو تصحيحية).");
        var description = r.Description?.Trim() ?? string.Empty;
        if (description.Length == 0) errors.Add("وصف العمل المطلوب مطلوب.");
        if (r.LaborCost < 0 || r.ExternalCost < 0 || r.DowntimeHours < 0 || r.MeterReading < 0) errors.Add("القيم لا تكون سالبة.");
        if (r.TechnicianEmployeeId is Guid tech && !await _db.Set<Employee>().AnyAsync(x => x.Id == tech, ct)) errors.Add("الفني غير موجود في شؤون الموظفين.");
        if (r.SupplierId is Guid supplier && !await _db.Set<Supplier>().AnyAsync(x => x.Id == supplier, ct)) errors.Add("الورشة (المورد) غير موجودة.");
        var parts = (r.Parts ?? new()).Where(p => p.ProductId != Guid.Empty).ToList();
        if (parts.Count > 0 && (r.WarehouseId is not Guid wh || !await _db.Set<Warehouse>().AnyAsync(w => w.Id == wh, ct)))
            errors.Add("مستودع قطع الغيار مطلوب.");
        var productIds = parts.Select(p => p.ProductId).Distinct().ToList();
        var products = await _db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        for (var k = 0; k < parts.Count; k++)
        {
            if (!products.TryGetValue(parts[k].ProductId, out var product)) errors.Add($"قطعة الغيار في السطر {k + 1} غير موجودة في الأصناف.");
            else if (!ProductItemTypes.TracksStock(product.ItemType)) errors.Add($"«{product.NameAr}» صنف بلا رصيد مخزني؛ لا يُصرف كقطعة غيار.");
            if (parts[k].Quantity <= 0) errors.Add($"كمية قطعة الغيار في السطر {k + 1} يجب أن تكون أكبر من صفر.");
        }
        foreach (var code in new[] { r.ExpenseAccountCode, r.CreditAccountCode }.Where(c => !string.IsNullOrWhiteSpace(c)))
            if (!await _db.Set<Account>().AnyAsync(a => a.Code == code!.Trim() && a.IsActive, ct)) errors.Add($"الحساب {code} غير موجود في دليل الحسابات أو موقوف.");
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        o.EquipmentId = r.EquipmentId; o.Type = r.Type; o.ScheduledDate = date; o.MeterReading = r.MeterReading;
        o.Description = description[..Math.Min(description.Length, 1000)]; o.WorkDone = EquipmentService.Trim(r.WorkDone, 2000);
        o.TechnicianEmployeeId = r.TechnicianEmployeeId; o.SupplierId = r.SupplierId; o.WarehouseId = r.WarehouseId;
        o.LaborCost = DocumentPricing.Round(r.LaborCost); o.ExternalCost = DocumentPricing.Round(r.ExternalCost); o.DowntimeHours = r.DowntimeHours;
        o.ExpenseAccountCode = EquipmentService.Trim(r.ExpenseAccountCode, 32); o.CreditAccountCode = EquipmentService.Trim(r.CreditAccountCode, 32);
        // المشروع من الإسناد النشط؛ التحميل عليه اختياري (الافتراضي مركز المعدة: صيانة المملوكة ضمن سعر ساعتها المحمّل)
        o.ContractId = await _db.Set<EquipmentAssignment>().AsNoTracking()
            .Where(a => a.EquipmentId == r.EquipmentId && a.Status == EquipmentAssignmentStatuses.Active).Select(a => (Guid?)a.ContractId).FirstOrDefaultAsync(ct);
        o.ChargeToProject = r.ChargeToProject && o.ContractId != null;
        var order = 0;
        foreach (var p in parts)
        {
            var product = products[p.ProductId];
            o.Parts.Add(new EquipmentMaintenancePart
            {
                ProductId = product.Id, ItemName = product.NameAr, Sku = product.Sku, Unit = product.Unit, Quantity = p.Quantity, SortOrder = ++order,
            });
        }
        // تقدير قبل الإكمال (القطع بمتوسط تكلفتها الحالي)؛ الفعلي يُثبَّت عند الإكمال
        o.PartsCost = DocumentPricing.Round(parts.Sum(p => p.Quantity * products[p.ProductId].AverageCost));
        o.TotalCost = o.PartsCost + o.LaborCost + o.ExternalCost;
    }

    private async Task<EquipmentMaintenanceOrder> LoadAsync(Guid id, bool track, CancellationToken ct)
    {
        var q = _db.Set<EquipmentMaintenanceOrder>().Include(o => o.Parts).AsQueryable();
        if (!track) q = q.AsNoTracking();
        return await q.FirstOrDefaultAsync(o => o.Id == id, ct) ?? throw new NotFoundException("أمر العمل غير موجود.");
    }

    private async Task<List<EquipmentMaintenanceOrderDto>> ToDtosAsync(List<EquipmentMaintenanceOrder> list, bool withStock, CancellationToken ct)
    {
        if (list.Count == 0) return new();
        var equipmentIds = list.Select(o => o.EquipmentId).Distinct().ToList();
        var equipment = await _db.Set<Equipment>().AsNoTracking().Where(e => equipmentIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => new { e.Code, e.NameAr }, ct);
        var employeeIds = list.Where(o => o.TechnicianEmployeeId != null).Select(o => o.TechnicianEmployeeId!.Value).Distinct().ToList();
        var employees = await _db.Set<Employee>().AsNoTracking().Where(e => employeeIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.NameAr, ct);
        var supplierIds = list.Where(o => o.SupplierId != null).Select(o => o.SupplierId!.Value).Distinct().ToList();
        var suppliers = await _db.Set<Supplier>().AsNoTracking().Where(s => supplierIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.NameAr, ct);
        var warehouseIds = list.Where(o => o.WarehouseId != null).Select(o => o.WarehouseId!.Value).Distinct().ToList();
        var warehouses = await _db.Set<Warehouse>().AsNoTracking().Where(w => warehouseIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.NameAr, ct);
        var contractIds = list.Where(o => o.ContractId != null).Select(o => o.ContractId!.Value).Distinct().ToList();
        var contracts = await _db.Set<CommercialContract>().AsNoTracking().Where(c => contractIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.ProjectName ?? c.Title, ct);

        var result = new List<EquipmentMaintenanceOrderDto>();
        foreach (var o in list)
        {
            var eq = equipment.GetValueOrDefault(o.EquipmentId);
            var dto = new EquipmentMaintenanceOrderDto
            {
                Id = o.Id, OrderNumber = o.OrderNumber, EquipmentId = o.EquipmentId, EquipmentCode = eq?.Code ?? string.Empty, EquipmentName = eq?.NameAr ?? string.Empty,
                Type = o.Type, Status = o.Status, ScheduledDate = o.ScheduledDate, StartedAt = o.StartedAt, CompletedAt = o.CompletedAt, MeterReading = o.MeterReading,
                Description = o.Description, WorkDone = o.WorkDone, TechnicianEmployeeId = o.TechnicianEmployeeId,
                TechnicianName = o.TechnicianEmployeeId is Guid t ? employees.GetValueOrDefault(t) : null, SupplierId = o.SupplierId,
                SupplierName = o.SupplierId is Guid s ? suppliers.GetValueOrDefault(s) : null, WarehouseId = o.WarehouseId,
                WarehouseName = o.WarehouseId is Guid w ? warehouses.GetValueOrDefault(w) : null, LaborCost = o.LaborCost, ExternalCost = o.ExternalCost,
                PartsCost = o.PartsCost, TotalCost = o.TotalCost, DowntimeHours = o.DowntimeHours, ContractId = o.ContractId,
                ProjectName = o.ContractId is Guid c ? contracts.GetValueOrDefault(c) : null, ChargeToProject = o.ChargeToProject,
                ExpenseAccountCode = o.ExpenseAccountCode, CreditAccountCode = o.CreditAccountCode, JournalEntryId = o.JournalEntryId, CompletedBy = o.CompletedBy,
                CancelReason = o.CancelReason, CreatedAt = o.CreatedAt,
            };
            foreach (var p in o.Parts.OrderBy(p => p.SortOrder))
                dto.Parts.Add(new EquipmentMaintenancePartDto
                {
                    Id = p.Id, ProductId = p.ProductId, ItemName = p.ItemName, Sku = p.Sku, Unit = p.Unit, Quantity = p.Quantity, UnitCost = p.UnitCost, TotalCost = p.TotalCost,
                    Stock = withStock && o.WarehouseId is Guid wid && o.Status is St.Open or St.InProgress ? await WarehouseStocks.QuantityAsync(_db, p.ProductId, wid, ct) : null,
                });
            result.Add(dto);
        }
        return result;
    }
}
