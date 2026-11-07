using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using Dg = ERP.Core.Models.Accounting.ProjectMaterialDiagnostics;
using St = ERP.Core.Models.Accounting.ProjectMaterialPlanStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// احتياجات مواد المشروع (العقد هو المشروع): خطة واحدة لكل عقد مقاولات بجدول كميات، أسطرها مواد مكتبة المقاولات بوحدتها،
/// والصنف المخزني من ربط المادة (اختياري؛ غير المربوطة لا تُصرف ولا تُشترى حتى تُربط). الاشتقاق من تحليل بنود المكتبة بكميات
/// العقد (<see cref="ProjectMaterialAnalysis"/>) مع تشخيص كل سطر، والتحرير في المسودة فقط، ثم التقديم والاعتماد (أو الإعادة بسبب).
/// المعتمدة نسخة ثابتة الكميات لا تتبع تعديل المكتبة.
/// الصلاحيات (الاستلام للإعداد والتقديم كالمستخلصات، والاعتماد للاعتماد والإعادة) ونطاق السجل يفحصها المتحكم على سجل العقد.
/// </summary>
public class ProjectMaterialService : IProjectMaterialService
{
    private const int Decimals = 4;

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
            Lines = await ToLinesAsync(contract, lines.Select(l => new LineSource(l.Id, l.BoqItemId, l.SubItemId, l.ResourceId, l.ProductId,
                l.Unit, l.Source, l.BaselineQuantity, l.Notes)).ToList(), ct),
        };
        dto.EstimatedCost = dto.Lines.Sum(l => l.EstimatedCost);
        return dto;
    }

    /// <summary>
    /// الاشتقاق: كل مواد تحليل بنود المكتبة بكميات العقد، حتى غير المربوطة بصنف مخزني (تُعلَّم في التشخيص)، مجمّعة لكل
    /// (بند، بند فرعي، مادة) بوحدة المادة. يعيد تشخيص كل سطر في جدول الكميات: اشتُق، أو اشتُق بتنبيه، أو تُخطي بسببه.
    /// </summary>
    public async Task<DerivedProjectMaterialsDto> DeriveAsync(Guid contractId, CancellationToken ct = default)
    {
        var contract = await LoadContractAsync(contractId, ct);
        var analysis = await ProjectMaterialAnalysis.LoadAsync(_db, contract, ct);
        var results = analysis.AnalyzeAll().ToList();
        var stock = await StockView.LoadAsync(_db, results.SelectMany(r => r.Materials).Select(m => m.Resource.ProductId), ct);

        var result = new DerivedProjectMaterialsDto();
        if (!analysis.HasLibraryLines) result.Warnings.Add(Messages.ProjectMaterialNoLibraryLines);
        var sources = new List<LineSource>();
        foreach (var r in results)
        {
            // البند المجموعة بلا مواد مباشرة: فروعه في العقد تحمل موادها وتشخيصها
            if (r.Line.IsGroup && r.SkipReason == Dg.NoMaterials) continue;
            var diag = new ProjectMaterialDiagnosticDto
            {
                LineId = r.Line.Id, BoqItemId = r.BoqItemId, SubItemId = r.SubItemId, LineCode = r.Line.LineCode, Description = r.Line.Description,
                IsSubItem = r.Line.IsSubItem, Reason = r.SkipReason, MaterialCount = r.Materials.Count, OtherResourceCount = r.OtherResourceCount,
            };
            foreach (var m in r.Materials)
            {
                var product = m.Resource.ProductId is Guid pid ? stock.Products.Find(pid) : null;
                if (product == null)
                    diag.Issues.Add(new ProjectMaterialIssueDto { Code = Dg.NotStocked, ResourceId = m.Resource.Id, ResourceName = m.Resource.NameAr, Unit = m.Resource.Unit });
                else if (stock.Convert(1, m.Resource.Unit, product.Unit) == null)
                    diag.Issues.Add(new ProjectMaterialIssueDto
                    {
                        Code = Dg.UnitMismatch, ResourceId = m.Resource.Id, ResourceName = m.Resource.NameAr, Unit = m.Resource.Unit, ProductUnit = product.Unit,
                    });
                var qty = Math.Round(m.Quantity, Decimals);
                if (qty > 0)
                    sources.Add(new LineSource(null, r.BoqItemId, r.SubItemId, m.Resource.Id, m.Resource.ProductId, m.Resource.Unit,
                        ProjectMaterialSources.Derived, qty, null));
            }
            diag.Status = r.SkipReason != null ? Dg.Skipped : diag.Issues.Count > 0 ? Dg.Warning : Dg.Derived;
            result.Diagnostics.Add(diag);
        }
        result.Lines = await ToLinesAsync(contract, sources, ct, analysis.Resources);
        return result;
    }

    /// <summary>
    /// مواد سطر في جدول الكميات: تحليل بنده في المكتبة بكمية مقترحة ومنشئها، ومع <paramref name="all"/> أو لسطر بلا تحليل
    /// تُضاف مواد المكتبة الفعّالة كلها (بلا كمية مقترحة لما ليس في التحليل).
    /// </summary>
    public async Task<ProjectMaterialCandidatesDto> CandidatesAsync(Guid contractId, Guid lineId, bool all, CancellationToken ct = default)
    {
        var contract = await LoadContractAsync(contractId, ct);
        var line = contract.Items.FirstOrDefault(i => i.Id == lineId) ?? throw new NotFoundException(Messages.ProjectMaterialLineNotFound);
        var analysis = await ProjectMaterialAnalysis.LoadAsync(_db, contract, ct);
        var r = analysis.Analyze(line);
        var library = all || r.SkipReason != null;
        var picked = r.Materials.Select(m => (Resource: m.Resource, Material: (ProjectMaterialAnalysis.Material?)m)).ToList();
        if (library)
            picked.AddRange(analysis.LibraryMaterials().Where(res => r.Materials.All(m => m.Resource.Id != res.Id))
                .Select(res => (Resource: res, Material: (ProjectMaterialAnalysis.Material?)null)));
        var stock = await StockView.LoadAsync(_db, picked.Select(p => p.Resource.ProductId), ct);

        return new ProjectMaterialCandidatesDto
        {
            LineId = line.Id, BoqItemId = r.BoqItemId, SubItemId = r.SubItemId, Reason = r.SkipReason,
            Scope = library ? ProjectMaterialCandidateScopes.Library : ProjectMaterialCandidateScopes.Item,
            Candidates = picked.Select(p =>
            {
                var product = p.Resource.ProductId is Guid pid ? stock.Products.Find(pid) : null;
                return new ProjectMaterialCandidateDto
                {
                    ResourceId = p.Resource.Id, ResourceCode = p.Resource.Code, ResourceName = p.Resource.NameAr, Unit = p.Resource.Unit,
                    UnitCost = p.Resource.StandardCost, IsStockLinked = product != null, ProductId = product?.Id, Sku = product?.Sku,
                    ProductName = product?.Name, ProductUnit = product?.Unit, InItemAnalysis = p.Material != null,
                    SuggestedQuantity = p.Material is { } m ? Math.Round(m.Quantity, Decimals) : null,
                    Origins = p.Material?.Origins.Select(o => new ProjectMaterialOriginDto
                    {
                        LibrarySubItem = o.LibrarySubItem, Composite = o.Composite, QuantityMode = o.QuantityMode, ConsumptionFactor = o.ConsumptionFactor,
                        FixedQuantity = o.FixedQuantity, WastePercent = o.WastePercent, OwnerQuantity = o.OwnerQuantity, Quantity = Math.Round(o.Quantity, Decimals),
                    }).ToList() ?? new(),
                };
            }).ToList(),
        };
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

            var resources = await ValidateLinesAsync(contract, request.Lines, token);
            foreach (var old in plan.Lines.ToList())
            {
                plan.Lines.Remove(old);
                _db.Remove(old);
            }
            var order = 0;
            foreach (var l in request.Lines)
            {
                var r = resources[l.ResourceId!.Value];
                plan.Lines.Add(new ProjectMaterialRequirement
                {
                    ContractId = contractId, BoqItemId = l.BoqItemId, SubItemId = l.SubItemId, ResourceId = r.Id, ProductId = r.ProductId,
                    Unit = r.Unit, BaselineQuantity = Math.Round(l.BaselineQuantity, Decimals), Source = l.Source,
                    Notes = string.IsNullOrWhiteSpace(l.Notes) ? null : l.Notes.Trim(), SortOrder = ++order,
                });
            }
            plan.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
            await _db.SaveChangesAsync(token);
            return await GetAsync(contractId, token);
        }, ct);

    public Task<ProjectMaterialPlanDto> SubmitAsync(Guid contractId, CancellationToken ct = default)
        => ChangeAsync(contractId, St.Draft, plan =>
        {
            if (plan.Lines.Count == 0) throw new ValidationFailedException(Messages.ProjectMaterialPlanEmpty);
            // سطر قديم بلا مادة مكتبة يُستكمل في المسودة قبل التقديم
            if (plan.Lines.Any(l => l.ResourceId == null)) throw new ValidationFailedException(Messages.ProjectMaterialLegacyLines);
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

    /// <summary>
    /// الأسطر صالحة: مادة مكتبة موجودة من نوع مادة (لا صنف مخزني مباشر) وكمية موجبة، والبند من جدول كميات العقد، والفرعي تحت
    /// بنده، ولا تكرار للمادة على البند نفسه. يعيد مواد الأسطر.
    /// </summary>
    private async Task<Dictionary<Guid, ConstructionResource>> ValidateLinesAsync(CommercialContract contract, List<SaveProjectMaterialRequirementDto> lines, CancellationToken ct)
    {
        var errors = new List<string>();
        var items = contract.Items.ToDictionary(i => i.Id);
        var ids = lines.Where(l => l.ResourceId.HasValue).Select(l => l.ResourceId!.Value).Distinct().ToList();
        var resources = await _db.Set<ConstructionResource>().AsNoTracking().Include(r => r.Components)
            .Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        var seen = new HashSet<(Guid?, Guid?, Guid)>();
        for (var n = 0; n < lines.Count; n++)
        {
            var l = lines[n];
            var row = n + 1;
            if (l.ResourceId is not Guid rid || rid == Guid.Empty || l.BaselineQuantity <= 0 || !ProjectMaterialSources.IsValid(l.Source))
            {
                errors.Add(string.Format(Messages.ProjectMaterialLineInvalid, row));
                continue;
            }
            if (!resources.TryGetValue(rid, out var resource) || !ProjectMaterialAnalysis.IsMaterial(resource))
                errors.Add(string.Format(Messages.ProjectMaterialResourceInvalid, row));
            if (l.BoqItemId is Guid boq && (!items.TryGetValue(boq, out var main) || main.IsSubItem))
                errors.Add(string.Format(Messages.ProjectMaterialBoqItemInvalid, row));
            if (l.SubItemId is Guid sub && (l.BoqItemId == null || !items.TryGetValue(sub, out var child) || !child.IsSubItem || child.ParentItemId != l.BoqItemId))
                errors.Add(string.Format(Messages.ProjectMaterialSubItemInvalid, row));
            if (!seen.Add((l.BoqItemId, l.SubItemId, rid))) errors.Add(string.Format(Messages.ProjectMaterialDuplicate, row));
            if (l.Notes?.Length > 500) errors.Add(string.Format(Messages.ProjectMaterialLineInvalid, row));
        }
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
        return resources;
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

    private sealed record LineSource(Guid? Id, Guid? BoqItemId, Guid? SubItemId, Guid? ResourceId, Guid? ProductId, string Unit,
        string Source, decimal Quantity, string? Notes);

    /// <summary>
    /// أسطر العرض: بيانات البند ومادة المكتبة، والصنف المربوط بالمادة حالياً (أو المحفوظ للسطر القديم) بالكمية المحوّلة لوحدته
    /// ورصيده (الكلي وفي مستودع المشروع)، والتكلفة التقديرية من تكلفة المادة الحالية في المكتبة.
    /// </summary>
    private async Task<List<ProjectMaterialRequirementDto>> ToLinesAsync(CommercialContract contract, List<LineSource> sources, CancellationToken ct,
        IReadOnlyDictionary<Guid, ConstructionResource>? known = null)
    {
        if (sources.Count == 0) return new();
        var items = contract.Items.ToDictionary(i => i.Id);
        var resources = known;
        if (resources == null)
        {
            var ids = sources.Where(s => s.ResourceId.HasValue).Select(s => s.ResourceId!.Value).Distinct().ToList();
            resources = await _db.Set<ConstructionResource>().AsNoTracking().Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        }
        ConstructionResource? Res(LineSource s) => s.ResourceId is Guid id ? resources.GetValueOrDefault(id) : null;
        Guid? ProductOf(LineSource s) => Res(s) is { } r ? r.ProductId : s.ProductId;
        var stock = await StockView.LoadAsync(_db, sources.Select(ProductOf), ct, contract.WarehouseId);

        return sources.Select((s, n) =>
        {
            var r = Res(s);
            var p = ProductOf(s) is Guid pid ? stock.Products.Find(pid) : null;
            var main = s.BoqItemId is Guid b ? items.GetValueOrDefault(b) : null;
            var sub = s.SubItemId is Guid si ? items.GetValueOrDefault(si) : null;
            var unit = string.IsNullOrWhiteSpace(s.Unit) ? r?.Unit ?? p?.Unit ?? string.Empty : s.Unit;
            var productQty = p == null ? null : stock.Convert(s.Quantity, unit, p.Unit);
            // التكلفة من المادة بوحدتها؛ والسطر القديم بلا مادة من تكلفة صنفه
            var resourceQty = r == null ? null : stock.Convert(s.Quantity, unit, r.Unit);
            var cost = resourceQty is decimal rq ? DocumentPricing.Round(rq * r!.StandardCost)
                : productQty is decimal pq ? DocumentPricing.Round(pq * p!.Cost) : 0;
            return new ProjectMaterialRequirementDto
            {
                Id = s.Id, BoqItemId = s.BoqItemId, BoqItemCode = main?.LineCode, BoqItemDescription = main?.Description,
                SubItemId = s.SubItemId, SubItemCode = sub?.LineCode, SubItemDescription = sub?.Description,
                ResourceId = r?.Id ?? s.ResourceId, ResourceCode = r?.Code, ResourceName = r?.NameAr ?? p?.Name, Unit = unit,
                Source = s.Source, BaselineQuantity = s.Quantity, ApprovedQuantity = s.Quantity,
                UnitCost = s.Quantity == 0 ? 0 : Math.Round(cost / s.Quantity, Decimals), EstimatedCost = cost,
                IsStockLinked = p != null, ProductId = p?.Id, Sku = p?.Sku, ProductName = p?.Name, ProductUnit = p?.Unit,
                ProductQuantity = productQty is decimal q ? Math.Round(q, Decimals) : null,
                OnHandQuantity = p == null ? null : stock.OnHand.GetValueOrDefault(p.Id),
                ProjectWarehouseQuantity = p == null ? null : stock.InWarehouse.GetValueOrDefault(p.Id),
                Notes = s.Notes, SortOrder = n + 1,
            };
        }).ToList();
    }

    /// <summary>الأصناف المخزنية المربوطة بالمواد ووحداتها ورصيدها (الكلي وفي مستودع محدد).</summary>
    private sealed class StockView
    {
        public required BoqProducts Products { get; init; }
        public required BoqUnits Units { get; init; }
        public Dictionary<Guid, decimal> OnHand { get; init; } = new();
        public Dictionary<Guid, decimal> InWarehouse { get; init; } = new();

        public static async Task<StockView> LoadAsync(ErpDbContext db, IEnumerable<Guid?> productIds, CancellationToken ct, Guid? warehouseId = null)
        {
            var ids = productIds.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
            var products = await BoqProducts.LoadAsync(db, ids.Select(i => (Guid?)i), ct);
            var units = await BoqUnits.LoadAsync(db, ct);
            if (ids.Count == 0) return new StockView { Products = products, Units = units };
            var stock = await db.Set<WarehouseStock>().AsNoTracking().Where(s => ids.Contains(s.ItemId))
                .Select(s => new { s.ItemId, s.WarehouseId, s.Quantity }).ToListAsync(ct);
            return new StockView
            {
                Products = products, Units = units,
                OnHand = stock.GroupBy(s => s.ItemId).ToDictionary(g => g.Key, g => g.Sum(s => s.Quantity)),
                InWarehouse = warehouseId is Guid wh
                    ? stock.Where(s => s.WarehouseId == wh).GroupBy(s => s.ItemId).ToDictionary(g => g.Key, g => g.Sum(s => s.Quantity))
                    : new(),
            };
        }

        /// <summary>الكمية بوحدة الهدف (التحويل المعرّف، أو الوحدة نفسها)، أو فارغة إن لم تتحول.</summary>
        public decimal? Convert(decimal quantity, string from, string to)
            => Units.Convert(quantity, from, to)
               ?? (string.Equals(from.Trim(), to.Trim(), StringComparison.OrdinalIgnoreCase) ? quantity : null);
    }
}
