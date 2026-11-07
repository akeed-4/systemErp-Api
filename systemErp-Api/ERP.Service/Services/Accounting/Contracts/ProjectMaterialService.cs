using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using St = ERP.Core.Models.Accounting.ProjectMaterialPlanStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// احتياجات مواد المشروع (العقد هو المشروع): خطة واحدة لكل عقد مقاولات بجدول كميات. الاشتقاق من تحليل بنود المكتبة بكميات
/// العقد، والتحرير في المسودة فقط، ثم التقديم والاعتماد (أو الإعادة بسبب). المعتمدة نسخة ثابتة لا تتبع تعديل المكتبة.
/// الصلاحيات (التعديل للإعداد والتقديم، الاعتماد للاعتماد والإعادة) ونطاق السجل يفحصها المتحكم على سجل العقد.
/// </summary>
public class ProjectMaterialService : IProjectMaterialService
{
    private const int Decimals = 4;
    private const int MaxCompositeDepth = 8;

    private readonly ErpDbContext _db;
    private readonly ITransactionRunner _tx;
    private readonly ICurrentUser _user;

    public ProjectMaterialService(ErpDbContext db, ITransactionRunner tx, ICurrentUser user)
    {
        _db = db; _tx = tx; _user = user;
    }

    // ---------- القراءة ----------

    public async Task<ProjectMaterialPlanDto> GetAsync(Guid contractId, CancellationToken ct = default)
    {
        var contract = await LoadContractAsync(contractId, ct);
        var plan = await _db.Set<ProjectMaterialPlan>().AsNoTracking().Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.ContractId == contractId, ct);
        var lines = (plan?.Lines ?? new List<ProjectMaterialRequirement>()).OrderBy(l => l.SortOrder).ToList();
        var dto = new ProjectMaterialPlanDto
        {
            Id = plan?.Id, ContractId = contract.Id, ContractNumber = contract.ContractNumber, ProjectName = contract.ProjectName,
            WarehouseId = contract.WarehouseId, Status = plan?.Status ?? St.Draft, Notes = plan?.Notes, ReturnNote = plan?.ReturnNote,
            SubmittedAt = plan?.SubmittedAt, SubmittedBy = plan?.SubmittedBy, ApprovedAt = plan?.ApprovedAt, ApprovedBy = plan?.ApprovedBy,
            Lines = await ToLinesAsync(contract, lines.Select(l => new LineSource(l.Id, l.BoqItemId, l.SubItemId, l.ProductId, l.ResourceId,
                l.Source, l.BaselineQuantity, l.Notes)).ToList(), ct),
        };
        dto.EstimatedCost = dto.Lines.Sum(l => l.EstimatedCost);
        return dto;
    }

    /// <summary>
    /// الاشتقاق: لكل سطر من المكتبة احتياجات بنده بكمية العقد = (لكل وحدة: الكمية × معامل الاستهلاك، أو الثابتة) × (1 + الهدر).
    /// البند الفرعي في العقد يأخذ احتياجات بنده الفرعي في المكتبة، والبند الرئيسي احتياجاته المباشرة، والبند بلا فروع في العقد
    /// احتياجات كل فروعه في المكتبة بكمياتها. المورد المركّب يُفك لمكوناته، والمادة تُحوَّل لوحدة صنفها المخزني.
    /// </summary>
    public async Task<DerivedProjectMaterialsDto> DeriveAsync(Guid contractId, CancellationToken ct = default)
    {
        var contract = await LoadContractAsync(contractId, ct);
        var warnings = new List<string>();
        var ordered = contract.Items.OrderBy(i => i.SortOrder).ToList();
        var byId = ordered.ToDictionary(i => i.Id);

        var workIds = ordered.Where(i => i.WorkItemId.HasValue).Select(i => i.WorkItemId!.Value).Distinct().ToList();
        if (workIds.Count == 0)
            return new DerivedProjectMaterialsDto { Warnings = { Messages.ProjectMaterialNoLibraryLines } };
        var works = await _db.Set<WorkItem>().AsNoTracking().Include(w => w.SubItems).Include(w => w.Components)
            .Where(w => workIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, ct);
        var resources = await _db.Set<ConstructionResource>().AsNoTracking().Include(r => r.Components).ToDictionaryAsync(r => r.Id, ct);

        // الإسهامات بوحدة المورد، ثم تُحوَّل لوحدة الصنف وتُجمع لكل (بند، بند فرعي، صنف)
        var raw = new List<(Guid? Boq, Guid? Sub, ConstructionResource Resource, decimal Qty)>();
        var notLinked = new HashSet<string>();

        void Expand(Guid? boq, Guid? sub, Guid resourceId, decimal qty, int depth)
        {
            if (qty <= 0 || depth > MaxCompositeDepth || !resources.TryGetValue(resourceId, out var r)) return;
            if (r.CostSource == ResourceCostSources.Composite && r.Components.Count > 0)
            {
                foreach (var c in r.Components) Expand(boq, sub, c.ComponentResourceId, qty * c.Quantity * (1 + c.WastePercent / 100m), depth + 1);
                return;
            }
            if (r.ResourceType != BoqResourceTypes.Material) return; // العمالة والمعدات والباطن ليست مواد مخزنية
            if (r.ProductId == null) { notLinked.Add(r.NameAr); return; }
            raw.Add((boq, sub, r, qty));
        }

        void AddComponents(Guid? boq, Guid? sub, IEnumerable<WorkItemComponent> components, decimal ownerQty)
        {
            foreach (var c in components)
            {
                var baseQty = c.QuantityMode == BoqQuantityModes.PerUnit ? ownerQty * c.ConsumptionFactor : c.FixedQuantity;
                Expand(boq, sub, c.ResourceId, baseQty * (1 + c.WastePercent / 100m), 0);
            }
        }

        foreach (var line in ordered)
        {
            if (line.IsSubItem)
            {
                if (line.ParentItemId is not Guid parentId || !byId.TryGetValue(parentId, out var parent)
                    || parent.WorkItemId is not Guid parentWork || line.WorkSubItemId is not Guid librarySub
                    || !works.TryGetValue(parentWork, out var parentItem)) continue;
                AddComponents(parent.Id, line.Id, parentItem.Components.Where(c => c.SubItemId == librarySub), line.Quantity);
                continue;
            }
            if (line.WorkItemId is not Guid workId) continue;
            if (!works.TryGetValue(workId, out var work))
            {
                warnings.Add(string.Format(Messages.ProjectMaterialWorkItemMissing, Label(line)));
                continue;
            }
            AddComponents(line.Id, null, work.Components.Where(c => c.SubItemId == null), line.Quantity);
            if (line.IsGroup) continue; // فروعه في العقد تحمل احتياجات بنودها الفرعية
            foreach (var s in work.SubItems)
            {
                var subQty = s.QuantityMode == BoqQuantityModes.PerUnit ? line.Quantity * s.QuantityFactor : s.Quantity;
                AddComponents(line.Id, null, work.Components.Where(c => c.SubItemId == s.Id), subQty);
            }
        }

        var units = await BoqUnits.LoadAsync(_db, ct);
        var products = await BoqProducts.LoadAsync(_db, raw.Select(x => x.Resource.ProductId), ct);
        var merged = new Dictionary<(Guid?, Guid?, Guid), (decimal Qty, Guid ResourceId)>();
        var order = new List<(Guid?, Guid?, Guid)>();
        var mismatched = new HashSet<string>();
        foreach (var (boq, sub, r, qty) in raw)
        {
            var product = products.Find(r.ProductId!.Value);
            if (product == null) { notLinked.Add(r.NameAr); continue; }
            var converted = units.Convert(qty, r.Unit, product.Unit)
                ?? (string.Equals(r.Unit.Trim(), product.Unit.Trim(), StringComparison.OrdinalIgnoreCase) ? qty : null);
            if (converted == null)
            {
                mismatched.Add(string.Format(Messages.ProjectMaterialUnitMismatch, r.NameAr, r.Unit, product.Unit));
                continue;
            }
            var key = (boq, sub, product.Id);
            if (merged.TryGetValue(key, out var existing)) merged[key] = (existing.Qty + converted.Value, existing.ResourceId);
            else { merged[key] = (converted.Value, r.Id); order.Add(key); }
        }
        warnings.AddRange(notLinked.Select(n => string.Format(Messages.ProjectMaterialResourceNotLinked, n)));
        warnings.AddRange(mismatched);

        var sources = order.Select(k => new LineSource(null, k.Item1, k.Item2, k.Item3, merged[k].ResourceId,
            ProjectMaterialSources.Derived, Math.Round(merged[k].Qty, Decimals), null)).Where(s => s.Quantity > 0).ToList();
        return new DerivedProjectMaterialsDto { Lines = await ToLinesAsync(contract, sources, ct), Warnings = warnings };
    }

    // ---------- الكتابة ----------

    public Task<ProjectMaterialPlanDto> SaveAsync(Guid contractId, SaveProjectMaterialPlanDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var contract = await LoadContractAsync(contractId, token);
            EnsureOpen(contract);
            var plan = await _db.Set<ProjectMaterialPlan>().Include(p => p.Lines).FirstOrDefaultAsync(p => p.ContractId == contractId, token);
            if (plan == null)
            {
                plan = new ProjectMaterialPlan { ContractId = contractId };
                _db.Add(plan);
            }
            else RequireStatus(plan, St.Draft);

            var units = await ValidateLinesAsync(contract, request.Lines, token);
            foreach (var old in plan.Lines.ToList())
            {
                plan.Lines.Remove(old);
                _db.Remove(old);
            }
            var order = 0;
            foreach (var l in request.Lines)
                plan.Lines.Add(new ProjectMaterialRequirement
                {
                    ContractId = contractId, BoqItemId = l.BoqItemId, SubItemId = l.SubItemId, ProductId = l.ProductId, ResourceId = l.ResourceId,
                    Unit = units[l.ProductId], BaselineQuantity = Math.Round(l.BaselineQuantity, Decimals), Source = l.Source,
                    Notes = string.IsNullOrWhiteSpace(l.Notes) ? null : l.Notes.Trim(), SortOrder = ++order,
                });
            plan.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
            await _db.SaveChangesAsync(token);
            return await GetAsync(contractId, token);
        }, ct);

    public Task<ProjectMaterialPlanDto> SubmitAsync(Guid contractId, CancellationToken ct = default)
        => ChangeAsync(contractId, St.Draft, plan =>
        {
            if (plan.Lines.Count == 0) throw new ValidationFailedException(Messages.ProjectMaterialPlanEmpty);
            plan.Status = St.Submitted; plan.ReturnNote = null;
            plan.SubmittedAt = DateTime.UtcNow; plan.SubmittedBy = _user.Name ?? "system";
        }, ct);

    public Task<ProjectMaterialPlanDto> ApproveAsync(Guid contractId, CancellationToken ct = default)
        => ChangeAsync(contractId, St.Submitted, plan =>
        {
            plan.Status = St.Approved;
            plan.ApprovedAt = DateTime.UtcNow; plan.ApprovedBy = _user.Name ?? "system"; plan.ApprovedByUserId = _user.UserId;
        }, ct);

    public Task<ProjectMaterialPlanDto> ReturnAsync(Guid contractId, ProjectMaterialPlanActionDto request, CancellationToken ct = default)
    {
        var note = request.Note?.Trim();
        if (string.IsNullOrWhiteSpace(note)) throw new ValidationFailedException(Messages.ProjectMaterialReturnNoteRequired);
        return ChangeAsync(contractId, St.Submitted, plan =>
        {
            plan.Status = St.Draft; plan.ReturnNote = note;
            plan.SubmittedAt = null; plan.SubmittedBy = null;
        }, ct);
    }

    /// <summary>انتقال الخطة من حالة محددة (داخل معاملة على العقد المفتوح).</summary>
    private Task<ProjectMaterialPlanDto> ChangeAsync(Guid contractId, string from, Action<ProjectMaterialPlan> apply, CancellationToken ct)
        => _tx.RunAsync(async token =>
        {
            var contract = await LoadContractAsync(contractId, token);
            EnsureOpen(contract);
            var plan = await _db.Set<ProjectMaterialPlan>().Include(p => p.Lines).FirstOrDefaultAsync(p => p.ContractId == contractId, token)
                ?? throw new NotFoundException(Messages.ProjectMaterialPlanNotFound);
            RequireStatus(plan, from);
            apply(plan);
            await _db.SaveChangesAsync(token);
            return await GetAsync(contractId, token);
        }, ct);

    // ---------- التحقق ----------

    /// <summary>الأسطر صالحة: صنف موجود وكمية موجبة، والبند من جدول كميات العقد، والفرعي تحت بنده، ولا تكرار. يعيد وحدة كل صنف.</summary>
    private async Task<Dictionary<Guid, string>> ValidateLinesAsync(CommercialContract contract, List<SaveProjectMaterialRequirementDto> lines, CancellationToken ct)
    {
        var errors = new List<string>();
        var items = contract.Items.ToDictionary(i => i.Id);
        var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
        var units = await _db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Unit, ct);
        var seen = new HashSet<(Guid?, Guid?, Guid)>();
        for (var n = 0; n < lines.Count; n++)
        {
            var l = lines[n];
            var row = n + 1;
            if (l.ProductId == Guid.Empty || l.BaselineQuantity <= 0 || !ProjectMaterialSources.IsValid(l.Source))
            {
                errors.Add(string.Format(Messages.ProjectMaterialLineInvalid, row));
                continue;
            }
            if (!units.ContainsKey(l.ProductId)) errors.Add(string.Format(Messages.ProjectMaterialProductNotFound, row));
            if (l.BoqItemId is Guid boq && (!items.TryGetValue(boq, out var main) || main.IsSubItem))
                errors.Add(string.Format(Messages.ProjectMaterialBoqItemInvalid, row));
            if (l.SubItemId is Guid sub && (l.BoqItemId == null || !items.TryGetValue(sub, out var child) || !child.IsSubItem || child.ParentItemId != l.BoqItemId))
                errors.Add(string.Format(Messages.ProjectMaterialSubItemInvalid, row));
            if (!seen.Add((l.BoqItemId, l.SubItemId, l.ProductId))) errors.Add(string.Format(Messages.ProjectMaterialDuplicate, row));
            if (l.Notes?.Length > 500) errors.Add(string.Format(Messages.ProjectMaterialLineInvalid, row));
        }
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
        return units;
    }

    private static void RequireStatus(ProjectMaterialPlan plan, string status)
    {
        if (plan.Status != status) throw new ConflictException(string.Format(Messages.ProjectMaterialPlanStatusInvalid, plan.Status));
    }

    private static void EnsureOpen(CommercialContract c)
    {
        if (c.Stage is ContractStageDefinition.Closed or ContractStageDefinition.Cancelled)
            throw new ConflictException(Messages.ProjectMaterialsContractClosed);
    }

    /// <summary>العقد ببنوده، بشرط أن يكون عقد مقاولات بجدول كميات.</summary>
    private async Task<CommercialContract> LoadContractAsync(Guid contractId, CancellationToken ct)
    {
        var contract = await _db.Set<CommercialContract>().AsNoTracking().Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == contractId, ct)
            ?? throw new NotFoundException(Messages.ContractNotFound);
        if (!await CommercialContractService.IsBoqContractAsync(_db, contract.ContractType, ct))
            throw new ConflictException(Messages.ProjectMaterialsOnlyForBoq);
        return contract;
    }

    // ---------- البناء للعرض ----------

    private sealed record LineSource(Guid? Id, Guid? BoqItemId, Guid? SubItemId, Guid ProductId, Guid? ResourceId, string Source, decimal Quantity, string? Notes);

    /// <summary>أسطر العرض: بيانات البند والصنف، والتكلفة التقديرية، ورصيد الصنف (الكلي وفي مستودع المشروع).</summary>
    private async Task<List<ProjectMaterialRequirementDto>> ToLinesAsync(CommercialContract contract, List<LineSource> sources, CancellationToken ct)
    {
        if (sources.Count == 0) return new();
        var items = contract.Items.ToDictionary(i => i.Id);
        var products = await BoqProducts.LoadAsync(_db, sources.Select(s => (Guid?)s.ProductId), ct);
        var ids = sources.Select(s => s.ProductId).Distinct().ToList();
        var stock = await _db.Set<WarehouseStock>().AsNoTracking().Where(s => ids.Contains(s.ItemId))
            .Select(s => new { s.ItemId, s.WarehouseId, s.Quantity }).ToListAsync(ct);
        var onHand = stock.GroupBy(s => s.ItemId).ToDictionary(g => g.Key, g => g.Sum(s => s.Quantity));
        var inProject = contract.WarehouseId is Guid wh
            ? stock.Where(s => s.WarehouseId == wh).GroupBy(s => s.ItemId).ToDictionary(g => g.Key, g => g.Sum(s => s.Quantity))
            : new Dictionary<Guid, decimal>();

        return sources.Select((s, n) =>
        {
            var p = products.Find(s.ProductId);
            var main = s.BoqItemId is Guid b ? items.GetValueOrDefault(b) : null;
            var sub = s.SubItemId is Guid si ? items.GetValueOrDefault(si) : null;
            var cost = p?.Cost ?? 0;
            return new ProjectMaterialRequirementDto
            {
                Id = s.Id, BoqItemId = s.BoqItemId, BoqItemCode = main?.LineCode, BoqItemDescription = main?.Description,
                SubItemId = s.SubItemId, SubItemCode = sub?.LineCode, SubItemDescription = sub?.Description,
                ProductId = s.ProductId, Sku = p?.Sku ?? string.Empty, ProductName = p?.Name ?? string.Empty, Unit = p?.Unit ?? string.Empty,
                ResourceId = s.ResourceId, Source = s.Source, BaselineQuantity = s.Quantity, ApprovedQuantity = s.Quantity,
                UnitCost = cost, EstimatedCost = DocumentPricing.Round(s.Quantity * cost),
                OnHandQuantity = onHand.GetValueOrDefault(s.ProductId), ProjectWarehouseQuantity = inProject.GetValueOrDefault(s.ProductId),
                Notes = s.Notes, SortOrder = n + 1,
            };
        }).ToList();
    }

    private static string Label(CommercialContractItem i) => string.IsNullOrWhiteSpace(i.LineCode) ? i.Description : $"{i.LineCode} {i.Description}";
}
