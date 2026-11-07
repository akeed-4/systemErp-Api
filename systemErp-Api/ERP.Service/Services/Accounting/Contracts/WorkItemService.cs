using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// مكتبة البنود. الحفظ: التحقق (الاسم، الوحدة المعرّفة، الكميات، الموارد الموجودة)، ثم مزامنة صريحة للبنود الفرعية
/// والاحتياجات (المزامنة العامة مستوى واحد). القراءة: التكلفة من تكاليف الموارد الحالية بقواعد تحليل العقد نفسها:
/// كمية البند الفرعي (per_unit) = الكمية القياسية × معامله، وكمية الاحتياج = كمية مالكه × المعامل (أو الثابتة) × (1 + الهدر)،
/// وتكلفة البند الفرعي من احتياجاته إن وُجدت وإلا من تكلفة وحدته، وتكلفة وحدة البند = المجموع ÷ الكمية القياسية.
/// </summary>
public class WorkItemService : IWorkItemService
{
    private const int Decimals = 4;
    private readonly ErpDbContext _db;
    private readonly ITransactionRunner _tx;
    private readonly INumberSequenceService _numbers;

    public WorkItemService(ErpDbContext db, ITransactionRunner tx, INumberSequenceService numbers)
    {
        _db = db; _tx = tx; _numbers = numbers;
    }

    // ---------- القراءة ----------

    public async Task<List<WorkItemSummaryDto>> ListAsync(CancellationToken ct = default)
    {
        var items = await _db.Set<WorkItem>().AsNoTracking().Include(w => w.SubItems).Include(w => w.Components).AsSplitQuery()
            .OrderBy(w => w.SectionName).ThenBy(w => w.Code).ToListAsync(ct);
        var costs = await ResourceCostsAsync(items.SelectMany(w => w.Components).Select(c => c.ResourceId), ct);
        return items.Select(w => (WorkItemSummaryDto)Build(w, costs, full: false)).ToList();
    }

    public async Task<WorkItemDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var item = await LoadAsync(id, track: false, ct);
        var costs = await ResourceCostsAsync(item.Components.Select(c => c.ResourceId), ct);
        return Build(item, costs, full: true);
    }

    // ---------- الحفظ ----------

    public Task<WorkItemDto> CreateAsync(SaveWorkItemDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            await ValidateAsync(request, null, token);
            var item = new WorkItem();
            Apply(item, request);
            item.Code = string.IsNullOrWhiteSpace(request.Code) ? await NextCodeAsync(token) : request.Code.Trim();
            _db.Add(item);
            SyncSubItems(item, request.SubItems, tracked: true); // البند متتبَّع (Added): الأبناء بمعرّف مسبق يُضافون صراحةً
            SyncComponents(item, null, request.Components, tracked: true);
            await ApplyResourceCostsAsync(request, token);
            await SaveAsync(token);
            if (await ResourceCostRules.RecomputeCompositesAsync(_db, token) > 0) await SaveAsync(token);
            return await GetAsync(item.Id, token);
        }, ct);

    public Task<WorkItemDto> UpdateAsync(Guid id, SaveWorkItemDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var item = await LoadAsync(id, track: true, token);
            await ValidateAsync(request, item, token);
            Apply(item, request);
            if (!string.IsNullOrWhiteSpace(request.Code)) item.Code = request.Code.Trim();
            SyncSubItems(item, request.SubItems, tracked: true);
            SyncComponents(item, null, request.Components, tracked: true);
            await ApplyResourceCostsAsync(request, token);
            await SaveAsync(token);
            if (await ResourceCostRules.RecomputeCompositesAsync(_db, token) > 0) await SaveAsync(token);
            return await GetAsync(item.Id, token);
        }, ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var item = await LoadAsync(id, track: true, token);
            _db.Remove(item); // البنود الفرعية والاحتياجات تُحذف معه
            await SaveAsync(token);
        }, ct);

    private async Task ValidateAsync(SaveWorkItemDto d, WorkItem? existing, CancellationToken ct)
    {
        var errors = new List<string>();
        var name = d.NameAr?.Trim() ?? string.Empty;
        if (name.Length == 0) errors.Add(Messages.ArabicNameRequired);
        if (string.IsNullOrWhiteSpace(d.Unit)) errors.Add(Messages.WorkItemUnitRequired);
        if (d.StandardQuantity <= 0) errors.Add(Messages.WorkItemStandardQuantityPositive);
        if (!ContractBoqLineKinds.IsValid(d.LineKind)) errors.Add(Messages.BoqLineKindInvalid);
        if (!WorkItemPricingModes.IsValid(d.PricingMode)) errors.Add(Messages.WorkItemPricingInvalid);
        if (d.SellPrice < 0 || d.MarkupPercent < 0) errors.Add(Messages.WorkItemPriceNegative);
        d.SubItems ??= new(); d.Components ??= new();
        foreach (var s in d.SubItems)
        {
            s.Components ??= new();
            var fixedQty = s.QuantityMode == BoqQuantityModes.Fixed;
            if (string.IsNullOrWhiteSpace(s.Description) || string.IsNullOrWhiteSpace(s.Unit) || !BoqQuantityModes.IsValid(s.QuantityMode)
                || (fixedQty ? s.Quantity <= 0 : s.QuantityFactor <= 0) || s.UnitCost < 0)
                errors.Add(string.Format(Messages.BoqSubItemInvalid, name, s.Description));
        }
        var allComponents = d.Components.Concat(d.SubItems.SelectMany(s => s.Components)).ToList();
        if (allComponents.Any(c => c.ResourceId == Guid.Empty || !BoqQuantityModes.IsValid(c.QuantityMode)
                || (c.QuantityMode == BoqQuantityModes.Fixed ? c.FixedQuantity <= 0 : c.ConsumptionFactor <= 0)
                || c.WastePercent is < 0 or > 100 || c.UnitCost < 0))
            errors.Add(string.Format(Messages.WorkItemComponentInvalid, name));
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors.Distinct().ToList());

        var code = d.Code?.Trim();
        if (!string.IsNullOrEmpty(code) && await _db.Set<WorkItem>().AnyAsync(w => w.Code == code && (existing == null || w.Id != existing.Id), ct))
            throw new ConflictException(string.Format(Messages.WorkItemCodeExists, code));

        var units = await BoqUnits.LoadAsync(_db, ct);
        d.Unit = units.Resolve(d.Unit, name, errors);
        foreach (var s in d.SubItems) s.Unit = units.Resolve(s.Unit, name, errors);
        var ids = allComponents.Select(c => c.ResourceId).Distinct().ToList();
        if (ids.Count > 0 && await _db.Set<ConstructionResource>().CountAsync(r => ids.Contains(r.Id), ct) != ids.Count)
            errors.Add(Messages.WorkItemResourceNotFound);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors.Distinct().ToList());
    }

    private static void Apply(WorkItem item, SaveWorkItemDto d)
    {
        item.NameAr = d.NameAr.Trim();
        item.NameEn = d.NameEn?.Trim() ?? string.Empty;
        item.SectionName = Trim(d.SectionName, 200);
        item.Unit = d.Unit;
        item.UnitDescription = Trim(d.UnitDescription, 200);
        item.StandardQuantity = d.StandardQuantity;
        item.LineKind = d.LineKind;
        item.PricingMode = d.PricingMode;
        item.SellPrice = d.PricingMode == WorkItemPricingModes.Manual ? d.SellPrice : 0;
        item.MarkupPercent = d.PricingMode == WorkItemPricingModes.CostPlus ? d.MarkupPercent : 0;
        item.Notes = Trim(d.Notes, 1000);
        item.IsActive = d.IsActive;
    }

    private void SyncSubItems(WorkItem item, List<WorkItemSubItemDto> requested, bool tracked)
    {
        var existing = item.SubItems.ToDictionary(s => s.Id);
        var keep = new HashSet<Guid>();
        var order = 0;
        foreach (var d in requested)
        {
            if (d.Id == Guid.Empty || keep.Contains(d.Id) || !existing.TryGetValue(d.Id, out var s))
            {
                // المعرّف يُعيَّن هنا لتُربط به احتياجات البند الفرعي قبل الحفظ
                s = new WorkItemSubItem { Id = Guid.NewGuid(), WorkItemId = item.Id };
                item.SubItems.Add(s);
                if (tracked) _db.Add(s);
            }
            s.LineCode = Trim(d.LineCode, 32);
            s.Description = d.Description.Trim();
            s.Unit = d.Unit;
            s.QuantityMode = d.QuantityMode;
            s.QuantityFactor = d.QuantityMode == BoqQuantityModes.PerUnit ? d.QuantityFactor : 0;
            s.Quantity = d.QuantityMode == BoqQuantityModes.Fixed ? d.Quantity : 0;
            s.UnitCost = d.UnitCost;
            s.SortOrder = ++order;
            keep.Add(s.Id);
            SyncComponents(item, s.Id, d.Components, tracked);
        }
        foreach (var removed in existing.Values.Where(s => !keep.Contains(s.Id)).ToList())
        {
            SyncComponents(item, removed.Id, new(), tracked);
            item.SubItems.Remove(removed);
            if (tracked) _db.Remove(removed);
        }
    }

    private void SyncComponents(WorkItem item, Guid? subItemId, List<WorkItemComponentDto> requested, bool tracked)
    {
        var existing = item.Components.Where(c => c.SubItemId == subItemId).ToDictionary(c => c.Id);
        var keep = new HashSet<Guid>();
        var order = 0;
        foreach (var d in requested)
        {
            if (d.Id == Guid.Empty || keep.Contains(d.Id) || !existing.TryGetValue(d.Id, out var c))
            {
                c = new WorkItemComponent { Id = Guid.NewGuid(), WorkItemId = item.Id, SubItemId = subItemId };
                item.Components.Add(c);
                if (tracked) _db.Add(c);
            }
            c.ResourceId = d.ResourceId;
            c.QuantityMode = d.QuantityMode;
            c.ConsumptionFactor = d.QuantityMode == BoqQuantityModes.PerUnit ? d.ConsumptionFactor : 0;
            c.FixedQuantity = d.QuantityMode == BoqQuantityModes.Fixed ? d.FixedQuantity : 0;
            c.WastePercent = d.WastePercent;
            c.SortOrder = ++order;
            keep.Add(c.Id);
        }
        foreach (var removed in existing.Values.Where(c => !keep.Contains(c.Id)).ToList())
        {
            item.Components.Remove(removed);
            if (tracked) _db.Remove(removed);
        }
    }

    /// <summary>
    /// تكلفة المورد اليدوي المعدّلة من سطر الاحتياج تُحفظ على المورد نفسه (فتتبعها كل البنود). مصدر الصنف وشؤون الموظفين
    /// والمركّب يحسبه الخادم فلا يُعدَّل من هنا. التكرار في الطلب: آخر قيمة.
    /// </summary>
    private async Task ApplyResourceCostsAsync(SaveWorkItemDto d, CancellationToken ct)
    {
        var edits = d.Components.Concat(d.SubItems.SelectMany(s => s.Components))
            .Where(c => c.UnitCost.HasValue).GroupBy(c => c.ResourceId).ToDictionary(g => g.Key, g => g.Last().UnitCost!.Value);
        if (edits.Count == 0) return;
        var ids = edits.Keys.ToList();
        var now = DateTime.UtcNow;
        foreach (var r in await _db.Set<ConstructionResource>().Where(r => ids.Contains(r.Id) && r.CostSource == ResourceCostSources.Manual).ToListAsync(ct))
        {
            var cost = Math.Round(edits[r.Id], Decimals);
            if (cost == r.StandardCost) continue;
            r.StandardCost = cost;
            r.CostUpdatedAt = now;
        }
    }

    // ---------- الحساب ----------

    private sealed record ResourceInfo(string Code, string Name, string Type, string Unit, decimal Cost, string Source);

    private async Task<Dictionary<Guid, ResourceInfo>> ResourceCostsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var wanted = ids.Distinct().ToList();
        if (wanted.Count == 0) return new();
        return await _db.Set<ConstructionResource>().AsNoTracking().Where(r => wanted.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => new ResourceInfo(r.Code, r.NameAr, r.ResourceType, r.Unit, r.StandardCost, r.CostSource), ct);
    }

    private static WorkItemDto Build(WorkItem w, Dictionary<Guid, ResourceInfo> resources, bool full)
    {
        var qty = w.StandardQuantity > 0 ? w.StandardQuantity : 1m;
        var byOwner = w.Components.ToLookup(c => c.SubItemId);

        WorkItemComponentDto Component(WorkItemComponent c, decimal ownerQty)
        {
            var r = resources.GetValueOrDefault(c.ResourceId);
            var baseQty = c.QuantityMode == BoqQuantityModes.PerUnit ? ownerQty * c.ConsumptionFactor : c.FixedQuantity;
            var quantity = Math.Round(baseQty * (1 + c.WastePercent / 100m), Decimals);
            var unitCost = r?.Cost ?? 0;
            return new WorkItemComponentDto
            {
                Id = c.Id, ResourceId = c.ResourceId, QuantityMode = c.QuantityMode, ConsumptionFactor = c.ConsumptionFactor,
                FixedQuantity = c.FixedQuantity, WastePercent = c.WastePercent,
                ResourceCode = r?.Code ?? string.Empty, ResourceName = r?.Name ?? string.Empty, ResourceType = r?.Type ?? string.Empty, CostSource = r?.Source ?? string.Empty,
                Unit = r?.Unit ?? string.Empty, Quantity = quantity, UnitCost = unitCost, TotalCost = DocumentPricing.Round(quantity * unitCost),
            };
        }

        var subs = w.SubItems.OrderBy(s => s.SortOrder).Select(s =>
        {
            var sQty = s.QuantityMode == BoqQuantityModes.PerUnit ? Math.Round(qty * s.QuantityFactor, Decimals) : s.Quantity;
            var comps = byOwner[s.Id].OrderBy(c => c.SortOrder).Select(c => Component(c, sQty)).ToList();
            var total = comps.Count > 0 ? comps.Sum(c => c.TotalCost) : DocumentPricing.Round(sQty * s.UnitCost);
            return new WorkItemSubItemDto
            {
                Id = s.Id, LineCode = s.LineCode, Description = s.Description, Unit = s.Unit, QuantityMode = s.QuantityMode,
                QuantityFactor = s.QuantityFactor, Quantity = sQty, TotalCost = total, Components = comps,
                UnitCost = comps.Count > 0 ? (sQty > 0 ? Math.Round(total / sQty, Decimals) : 0) : s.UnitCost,
            };
        }).ToList();
        var direct = byOwner[null].OrderBy(c => c.SortOrder).Select(c => Component(c, qty)).ToList();

        var totalCost = DocumentPricing.Round(subs.Sum(s => s.TotalCost) + direct.Sum(c => c.TotalCost));
        var unitCost = Math.Round(totalCost / qty, Decimals);
        var sell = w.PricingMode == WorkItemPricingModes.CostPlus ? DocumentPricing.Round(unitCost * (1 + w.MarkupPercent / 100m)) : w.SellPrice;
        var margin = unitCost > 0 ? sell - unitCost : 0;

        var dto = new WorkItemDto
        {
            Id = w.Id, Code = w.Code, NameAr = w.NameAr, NameEn = w.NameEn, SectionName = w.SectionName, Unit = w.Unit,
            UnitDescription = w.UnitDescription, LineKind = w.LineKind, IsActive = w.IsActive, UpdatedAt = w.UpdatedAt ?? w.CreatedAt,
            SubItemCount = w.SubItems.Count, ComponentCount = w.Components.Count,
            UnitCost = unitCost, SellPrice = sell, Margin = margin, MarginPercent = unitCost > 0 && sell > 0 ? Math.Round(margin / sell * 100m, 2) : 0,
            StandardQuantity = w.StandardQuantity, PricingMode = w.PricingMode, ManualSellPrice = w.SellPrice, MarkupPercent = w.MarkupPercent,
            Notes = w.Notes, TotalCost = totalCost,
        };
        if (full) { dto.SubItems = subs; dto.Components = direct; }
        return dto;
    }

    // ---------- مساعدات ----------

    private async Task<WorkItem> LoadAsync(Guid id, bool track, CancellationToken ct)
    {
        var q = _db.Set<WorkItem>().Include(w => w.SubItems).Include(w => w.Components).AsSplitQuery();
        return await (track ? q : q.AsNoTracking()).FirstOrDefaultAsync(w => w.Id == id, ct)
            ?? throw new NotFoundException(string.Format(Messages.EntityNotFound, Messages.LabelWorkItem));
    }

    /// <summary>رمز تلقائي فريد (ITM-0001...) حين يُترك الرمز فارغاً.</summary>
    private async Task<string> NextCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var code = await _numbers.NextAsync("work_item", "ITM-", ct);
            if (!await _db.Set<WorkItem>().AnyAsync(w => w.Code == code, ct)) return code;
        }
        throw new ConflictException(string.Format(Messages.SaveConstraintViolation, Messages.LabelWorkItem));
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when ((ex.InnerException?.Message ?? ex.Message).Contains("constraint", StringComparison.OrdinalIgnoreCase)
            || (ex.InnerException?.Message ?? ex.Message).Contains("duplicate", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException(string.Format(Messages.SaveConstraintViolation, Messages.LabelWorkItem));
        }
    }

    private static string? Trim(string? value, int max)
    {
        var t = value?.Trim();
        return string.IsNullOrEmpty(t) ? null : t.Length > max ? t[..max] : t;
    }
}
