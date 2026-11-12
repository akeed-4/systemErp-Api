using System.Globalization;
using System.Text.Json;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using St = ERP.Core.Models.Accounting.ExecutionPlanStatuses;
using Src = ERP.Core.Models.Accounting.PlanItemSources;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// خطة تنفيذ المشروع من دراسة الجدوى المجمّدة (<see cref="ContractItemResourceCost"/>). قواعد التوليد:
/// <list type="bullet">
/// <item>المراحل: مرحلة لكل باب (قسم) في جدول الكميات بترتيب ظهوره، ووزنها حصتها من التكلفة، وتكلفتها تكلفة بنودها.
/// المراحل اليدوية والمعدّلة تبقى وبنودها لا تُكرر.</item>
/// <item>التواريخ: مدة العقد تُوزع على المراحل بالتتابع حسب ساعات عمالتها ومعداتها، معلَّمة «مقترحة»؛ بلا ساعات أو بلا مدة لا تُخترع
/// مدد: تأخذ المرحلة مدة العقد كاملة ويظهر تنبيه.</item>
/// <item>المواد: من أسطر التحليل المادية إلى خطة الاحتياجات (المشتقة تُستبدل واليدوية تبقى) بمرحلتها وتكلفتها ومعاملها.</item>
/// <item>العمالة والمعدات وغيرها: احتياجات مخططة بالساعات والتكلفة والعدد المقترح والملكية (المعدة «غير محددة» ما لم تحددها
/// المكتبة)؛ بلا موظفين ولا أصول ولا موارد مشروع.</item>
/// <item>الميزانية: تُملأ من التكلفة التقديرية إن كانت صفراً، ولا تُستبدل إلا بطلب صريح.</item>
/// </list>
/// الاعتماد يجمّد نسختي الدراسة وأساس الخطة، ويعتمد احتياجات المواد معه، وينقل الدراسة إلى «خطة معتمدة».
/// </summary>
public class ExecutionPlanService : IExecutionPlanService
{
    private const int Decimals = 4;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ErpDbContext _db;
    private readonly ITransactionRunner _tx;
    private readonly ICurrentUser _user;
    private readonly IContractCostAnalysisService _analysis;

    public ExecutionPlanService(ErpDbContext db, ITransactionRunner tx, ICurrentUser user, IContractCostAnalysisService analysis)
    {
        _db = db; _tx = tx; _user = user; _analysis = analysis;
    }

    // ---------- القراءة ----------

    public async Task<ExecutionPlanDto> GetAsync(Guid contractId, CancellationToken ct = default)
    {
        var contract = await ConstructionProjectService.LoadBoqContractAsync(_db, contractId, ct);
        var plan = await _db.Set<ProjectExecutionPlan>().AsNoTracking().FirstOrDefaultAsync(p => p.ContractId == contractId, ct);
        var profile = await _db.Set<ConstructionProjectProfile>().AsNoTracking().FirstOrDefaultAsync(p => p.ContractId == contractId, ct);
        var phases = await _db.Set<ProjectPhase>().AsNoTracking().Include(p => p.Items).Where(p => p.ContractId == contractId)
            .OrderBy(p => p.SortOrder).ToListAsync(ct);
        var materialPlan = await _db.Set<ProjectMaterialPlan>().AsNoTracking().Include(p => p.Lines).FirstOrDefaultAsync(p => p.ContractId == contractId, ct);
        var materials = materialPlan?.Lines.OrderBy(l => l.SortOrder).ToList() ?? new();
        var resources = await _db.Set<ProjectResourceRequirement>().AsNoTracking().Where(r => r.ContractId == contractId)
            .OrderBy(r => r.SortOrder).ToListAsync(ct);
        var costRows = await _db.Set<ContractItemResourceCost>().AsNoTracking().Where(r => r.ContractId == contractId).ToListAsync(ct);
        var members = await _db.Set<ProjectMember>().AsNoTracking().Where(m => m.ContractId == contractId).ToListAsync(ct);
        var materialIds = materials.Where(m => m.ResourceId != null).Select(m => m.ResourceId!.Value).Distinct().ToList();
        var library = await _db.Set<ConstructionResource>().AsNoTracking().Where(r => materialIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, ct);
        var supervisorIds = phases.Where(p => p.SupervisorUserId != null).Select(p => p.SupervisorUserId!.Value).Distinct().ToList();
        var users = supervisorIds.Count == 0 ? new Dictionary<Guid, string>()
            : await _db.Set<User>().AsNoTracking().Where(u => supervisorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        var snapshots = await _db.Set<ProjectSnapshot>().AsNoTracking().Where(s => s.ContractId == contractId)
            .OrderByDescending(s => s.Revision).ThenBy(s => s.Kind)
            .Select(s => new ProjectSnapshotInfoDto { Id = s.Id, Kind = s.Kind, Revision = s.Revision, TotalCost = s.TotalCost, ContractValue = s.ContractValue, TakenAt = s.TakenAt, TakenBy = s.TakenBy })
            .ToListAsync(ct);

        var lineHours = costRows.GroupBy(r => r.ContractItemId).ToDictionary(g => g.Key, g => g.Sum(Hours));
        var dto = new ExecutionPlanDto
        {
            ContractId = contractId, PlanId = plan?.Id, Status = plan?.Status ?? "none", Revision = plan?.Revision ?? 0,
            GeneratedAt = plan?.GeneratedAt, GeneratedBy = plan?.GeneratedBy, SubmittedAt = plan?.SubmittedAt, SubmittedBy = plan?.SubmittedBy,
            ApprovedAt = plan?.ApprovedAt, ApprovedBy = plan?.ApprovedBy, ReturnNote = plan?.ReturnNote, FeasibilityStatus = contract.FeasibilityStatus,
            Editable = IsEditable(contract, plan), MaterialPlanStatus = materialPlan?.Status ?? "none", Baselines = snapshots,
        };
        foreach (var p in phases)
        {
            var items = p.Items.Select(i => i.ContractItemId).ToList();
            dto.Phases.Add(new ExecutionPlanPhaseDto
            {
                Id = p.Id, Name = p.Name, Notes = p.Notes, Description = p.Description, PlannedStart = p.PlannedStart, PlannedEnd = p.PlannedEnd, DatesSuggested = p.DatesSuggested,
                WeightPercent = p.WeightPercent, PlannedCost = p.PlannedCost, Hours = Math.Round(items.Sum(i => lineHours.GetValueOrDefault(i)), 2),
                Source = p.Source, SupervisorUserId = p.SupervisorUserId, SupervisorName = p.SupervisorUserId is Guid sid ? users.GetValueOrDefault(sid) : null,
                PredecessorIds = ParseIds(p.PredecessorIds), ContractItemIds = items,
                MaterialCount = materials.Count(m => m.PhaseId == p.Id), ResourceCount = resources.Count(r => r.PhaseId == p.Id),
            });
        }
        dto.Materials = materials.Select(m => new ExecutionPlanMaterialDto
        {
            Id = m.Id, PhaseId = m.PhaseId, BoqItemId = m.BoqItemId, SubItemId = m.SubItemId, ResourceId = m.ResourceId,
            Name = m.ResourceId is Guid rid && library.TryGetValue(rid, out var lr) ? lr.NameAr : string.Empty, Unit = m.Unit,
            Quantity = m.BaselineQuantity, QuantityPerUnit = m.QuantityPerUnit, PlannedUnitCost = m.PlannedUnitCost,
            PlannedCost = DocumentPricing.Round(m.BaselineQuantity * m.PlannedUnitCost), Source = m.Source,
        }).ToList();
        dto.MaterialTotals = dto.Materials.GroupBy(m => (m.ResourceId, m.Unit)).Select(g => new ExecutionPlanMaterialTotalDto
        {
            ResourceId = g.Key.ResourceId, Name = g.First().Name, Unit = g.Key.Unit, Quantity = Math.Round(g.Sum(m => m.Quantity), Decimals),
            PlannedCost = DocumentPricing.Round(g.Sum(m => m.PlannedCost)), LineCount = g.Select(m => m.SubItemId ?? m.BoqItemId).Distinct().Count(),
        }).OrderBy(t => t.Name).ToList();
        dto.Resources = resources.Select(ToDto).ToList();
        // المورد المتكرر عبر البنود مجمّعاً (اليدوي بلا مورد يُجمع باسمه ووحدته)
        dto.ResourceTotals = resources.GroupBy(r => (r.ResourceId, r.ResourceType, Name: r.ResourceId == null ? r.Name : null, r.Unit))
            .Select(g => new ExecutionPlanResourceTotalDto
            {
                ResourceId = g.Key.ResourceId, ResourceType = g.Key.ResourceType, Name = g.First().Name, Unit = g.Key.Unit,
                Quantity = Math.Round(g.Sum(r => r.Quantity), Decimals), PlannedHours = Math.Round(g.Sum(r => r.PlannedHours), 2),
                PlannedCost = DocumentPricing.Round(g.Sum(r => r.PlannedCost)), LineCount = g.Where(r => r.ContractItemId != null).Select(r => r.ContractItemId).Distinct().Count(),
            })
            .OrderBy(t => Array.IndexOf(BoqResourceTypes.All.ToArray(), t.ResourceType)).ThenByDescending(t => t.PlannedHours).ThenBy(t => t.Name).ToList();

        var budget = dto.Budget;
        budget.ContractValue = contract.ContractValue; budget.StudyCost = contract.EstimatedCost; budget.EstimatedBudget = profile?.EstimatedBudget ?? 0;
        budget.MaterialsCost = DocumentPricing.Round(dto.Materials.Sum(m => m.PlannedCost));
        budget.LaborCost = DocumentPricing.Round(resources.Where(r => r.ResourceType == BoqResourceTypes.Labor).Sum(r => r.PlannedCost));
        budget.EquipmentCost = DocumentPricing.Round(resources.Where(r => r.ResourceType == BoqResourceTypes.Equipment).Sum(r => r.PlannedCost));
        budget.OtherCost = DocumentPricing.Round(resources.Where(r => r.ResourceType is not (BoqResourceTypes.Labor or BoqResourceTypes.Equipment)).Sum(r => r.PlannedCost));
        budget.PlannedCost = budget.MaterialsCost + budget.LaborCost + budget.EquipmentCost + budget.OtherCost;
        budget.LaborHours = Math.Round(resources.Where(r => r.ResourceType == BoqResourceTypes.Labor).Sum(r => r.PlannedHours), 2);
        budget.EquipmentHours = Math.Round(resources.Where(r => r.ResourceType == BoqResourceTypes.Equipment).Sum(r => r.PlannedHours), 2);
        budget.PlannedMargin = DocumentPricing.Round(budget.ContractValue - (budget.PlannedCost > 0 ? budget.PlannedCost : budget.StudyCost));

        dto.Warnings = Warnings(contract, plan, profile, phases, materials, materialPlan, resources, costRows, members, library, dto.Budget);
        // بند رئيسي بفروع واحتياجات مباشرة بلا سطر أعمال مباشرة: تكلفتها وساعاتها خارج الدراسة والخطة
        var analysis = await ProjectMaterialAnalysis.LoadAsync(_db, contract, ct);
        var unassigned = contract.Items.Count(i => i.IsGroup && analysis.Analyze(i).SkipReason == ProjectMaterialDiagnostics.GroupDirectUnassigned);
        if (unassigned > 0) dto.Warnings.Insert(0, new ExecutionPlanWarningDto { Code = "group_direct_unassigned", Count = unassigned, Section = "estimate" });
        return dto;
    }

    /// <summary>ما ينقص الخطة (رموز تترجمها الواجهة، بقسم معالجتها): لا يُخترع ما لا تحمله الدراسة.</summary>
    private static List<ExecutionPlanWarningDto> Warnings(CommercialContract contract, ProjectExecutionPlan? plan, ConstructionProjectProfile? profile,
        List<ProjectPhase> phases, List<ProjectMaterialRequirement> materials, ProjectMaterialPlan? materialPlan, List<ProjectResourceRequirement> resources,
        List<ContractItemResourceCost> costRows, List<ProjectMember> members, Dictionary<Guid, ConstructionResource> library, ExecutionPlanBudgetDto budget)
    {
        var w = new List<ExecutionPlanWarningDto>();
        void Add(string code, int count, string section, string? detail = null) { if (count > 0) w.Add(new() { Code = code, Count = count, Section = section, Detail = detail }); }
        var leaves = contract.Items.Where(i => !i.IsGroup).ToList();
        var priced = costRows.Select(r => r.ContractItemId).ToHashSet();
        var phased = phases.SelectMany(p => p.Items).Select(i => i.ContractItemId).ToHashSet();

        Add("no_plan", plan == null ? 1 : 0, "plan");
        Add("lines_not_priced", leaves.Count(l => !priced.Contains(l.Id)), "estimate");
        // مورد بتكلفة صفرية في التحليل المجمّد: الدراسة والخطة أقل من الحقيقة (عدد الموارد، والتفصيل أسماؤها)
        var zeroCost = costRows.Where(r => r.UnitCost <= 0 && r.Quantity > 0).GroupBy(r => r.ResourceId).Select(g => g.First().ResourceName).ToList();
        Add("resources_without_cost", zeroCost.Count, "estimate", string.Join("، ", zeroCost.Take(5)));
        Add("no_phases", phases.Count == 0 && plan != null ? 1 : 0, "plan");
        Add("lines_unphased", phases.Count > 0 ? leaves.Count(l => !phased.Contains(l.Id)) : 0, "setup");
        Add("no_contract_dates", contract.StartDate == null || contract.EndDate == null ? 1 : 0, "estimate");
        Add("dates_need_review", phases.Count(p => p.Source == Src.Derived && !p.DatesSuggested), "setup");
        Add("no_dependencies", phases.Count > 1 && phases.All(p => string.IsNullOrEmpty(p.PredecessorIds)) ? 1 : 0, "setup");
        var weights = phases.Sum(p => p.WeightPercent);
        Add("weights_total", phases.Count > 0 && weights != 100 ? 1 : 0, "setup", weights.ToString("0.##", CultureInfo.InvariantCulture));
        Add("no_manager", members.Any(m => m.Role == ProjectMemberRoles.ProjectManager) ? 0 : 1, "setup");
        Add("no_supervisor", phases.Count > 0 && !members.Any(m => m.Role == ProjectMemberRoles.SiteSupervisor) ? phases.Count(p => p.SupervisorUserId == null) : 0, "setup");
        Add("equipment_ownership_undecided", resources.Count(r => r.ResourceType == BoqResourceTypes.Equipment && (r.Ownership ?? "undecided") == "undecided"), "plan");
        Add("materials_not_stocked", materials.Count(m => m.ResourceId is Guid id && library.TryGetValue(id, out var r) && r.ProductId == null), "materials");
        Add("no_warehouse", contract.WarehouseId == null ? 1 : 0, "estimate");
        Add("budget_differs", profile != null && profile.EstimatedBudget > 0 && budget.PlannedCost > 0 && profile.EstimatedBudget != budget.PlannedCost ? 1 : 0, "plan",
            profile?.EstimatedBudget.ToString("0.##", CultureInfo.InvariantCulture));
        Add("materials_locked", materialPlan?.Status == ProjectMaterialPlanStatuses.Approved && plan?.Status != St.Approved ? 1 : 0, "materials");
        return w;
    }

    // ---------- التوليد ----------

    public async Task<ExecutionPlanDto> GenerateAsync(Guid contractId, GenerateExecutionPlanDto request, CancellationToken ct = default)
    {
        await _tx.RunAsync(async token =>
        {
            var contract = await ConstructionProjectService.LoadBoqContractAsync(_db, contractId, token, track: true);
            var plan = await _db.Set<ProjectExecutionPlan>().FirstOrDefaultAsync(p => p.ContractId == contractId, token);
            EnsureEditable(contract, plan);
            if (plan == null)
            {
                plan = new ProjectExecutionPlan { ContractId = contractId };
                _db.Add(plan);
            }
            plan.GeneratedAt = DateTime.UtcNow; plan.GeneratedBy = _user.Name;

            var profile = await _db.Set<ConstructionProjectProfile>().FirstOrDefaultAsync(p => p.ContractId == contractId, token);
            if (profile == null)
            {
                profile = new ConstructionProjectProfile { ContractId = contractId };
                _db.Add(profile);
            }
            var costRows = await _db.Set<ContractItemResourceCost>().AsNoTracking().Where(r => r.ContractId == contractId).OrderBy(r => r.SortOrder).ToListAsync(token);
            var lineHours = costRows.GroupBy(r => r.ContractItemId).ToDictionary(g => g.Key, g => g.Sum(Hours));
            var phaseOfLine = await GeneratePhasesAsync(contract, profile, lineHours, token);
            await GenerateMaterialsAsync(contract, costRows, phaseOfLine, token);
            await GenerateResourcesAsync(contract, profile, costRows, phaseOfLine, token);
            if (profile.EstimatedBudget <= 0 || request.OverwriteBudget) profile.EstimatedBudget = contract.EstimatedCost;
            await _db.SaveChangesAsync(token);
        }, ct);
        return await GetAsync(contractId, ct);
    }

    /// <summary>المراحل حسب أبواب جدول الكميات؛ يعيد مرحلة كل سطر (المولَّدة والمبقاة).</summary>
    private async Task<Dictionary<Guid, Guid>> GeneratePhasesAsync(CommercialContract contract, ConstructionProjectProfile profile,
        Dictionary<Guid, decimal> lineHours, CancellationToken ct)
    {
        var existing = await _db.Set<ProjectPhase>().Include(p => p.Items).Where(p => p.ContractId == contract.Id).OrderBy(p => p.SortOrder).ToListAsync(ct);
        var kept = existing.Where(p => p.Source != Src.Derived).ToList();
        _db.RemoveRange(existing.Where(p => p.Source == Src.Derived));
        var phaseOfLine = new Dictionary<Guid, Guid>();
        foreach (var p in kept) foreach (var i in p.Items) phaseOfLine[i.ContractItemId] = p.Id;

        var lines = contract.Items.ToDictionary(i => i.Id);
        var workIds = contract.Items.Where(i => i.WorkItemId != null).Select(i => i.WorkItemId!.Value).Distinct().ToList();
        var workSections = await _db.Set<WorkItem>().AsNoTracking().Where(w => workIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.SectionName, ct);
        string? Section(CommercialContractItem l)
        {
            var owner = l.IsSubItem && l.ParentItemId is Guid pid && lines.TryGetValue(pid, out var parent) ? parent : l;
            return Clean(l.SectionName) ?? Clean(owner.SectionName) ?? (owner.WorkItemId is Guid wid ? Clean(workSections.GetValueOrDefault(wid)) : null);
        }

        var groups = new List<(string Name, List<CommercialContractItem> Lines)>();
        foreach (var l in contract.Items.Where(i => !i.IsGroup && !phaseOfLine.ContainsKey(i.Id)).OrderBy(i => i.SortOrder))
        {
            var name = Section(l) ?? Messages.ExecutionPlanUnsectioned;
            var g = groups.FindIndex(x => x.Name == name);
            if (g < 0) groups.Add((name, new List<CommercialContractItem> { l })); else groups[g].Lines.Add(l);
        }

        // الأوزان: ما يبقى بعد المراحل المبقاة يُوزع على المولَّدة بحصة التكلفة (أو بالتساوي بلا تكلفة)
        var remaining = Math.Max(0, 100m - kept.Sum(p => p.WeightPercent));
        var costs = groups.Select(g => g.Lines.Sum(l => DocumentPricing.Round(l.RevisedQuantity * l.EstimatedUnitCost))).ToList();
        var totalCost = costs.Sum();
        var weights = groups.Select((_, k) => totalCost > 0 ? Math.Round(remaining * costs[k] / totalCost, 2) : groups.Count > 0 ? Math.Round(remaining / groups.Count, 2) : 0).ToList();
        if (weights.Count > 0) weights[^1] = Math.Max(0, remaining - weights.Take(weights.Count - 1).Sum());

        // التواريخ: مدة العقد بالتتابع حسب الساعات؛ بلا ساعات أو بلا مدة: المدة كاملة بلا اقتراح
        var hours = groups.Select(g => g.Lines.Sum(l => lineHours.GetValueOrDefault(l.Id))).ToList();
        var totalHours = hours.Sum();
        var start = (contract.StartDate ?? ResourceCostRules.Today).Date;
        var end = (contract.EndDate ?? contract.StartDate ?? ResourceCostRules.Today).Date;
        if (end < start) end = start;
        var days = (end - start).Days + 1;
        var suggest = totalHours > 0 && contract.StartDate != null && contract.EndDate != null;
        var cursor = 0m;
        var order = kept.Count == 0 ? 0 : kept.Max(p => p.SortOrder);
        for (var k = 0; k < groups.Count; k++)
        {
            DateTime from = start, to = end;
            if (suggest)
            {
                var share = hours[k] / totalHours * days;
                var a = (int)Math.Floor(cursor);
                cursor += share;
                var b = k == groups.Count - 1 ? days - 1 : Math.Max(a, (int)Math.Ceiling(cursor) - 1);
                from = start.AddDays(Math.Min(a, days - 1)); to = start.AddDays(Math.Min(b, days - 1));
            }
            var phase = new ProjectPhase
            {
                ContractId = contract.Id, Name = groups[k].Name[..Math.Min(groups[k].Name.Length, 200)], PlannedStart = from, PlannedEnd = to,
                WeightPercent = weights[k], SortOrder = ++order, PlannedCost = costs[k], Source = Src.Derived, DatesSuggested = suggest,
            };
            foreach (var l in groups[k].Lines) phase.Items.Add(new ProjectPhaseItem { ContractItemId = l.Id });
            _db.Add(phase);
            foreach (var l in groups[k].Lines) phaseOfLine[l.Id] = phase.Id == Guid.Empty ? (phase.Id = Guid.NewGuid()) : phase.Id;
        }
        return phaseOfLine;
    }

    /// <summary>المواد من التحليل المجمّد إلى خطة الاحتياجات المسودة (المشتقة تُستبدل، واليدوية تبقى وتأخذ مرحلتها).</summary>
    private async Task GenerateMaterialsAsync(CommercialContract contract, List<ContractItemResourceCost> costRows, Dictionary<Guid, Guid> phaseOfLine, CancellationToken ct)
    {
        var plan = await _db.Set<ProjectMaterialPlan>().Include(p => p.Lines).FirstOrDefaultAsync(p => p.ContractId == contract.Id, ct);
        if (plan == null)
        {
            plan = new ProjectMaterialPlan { ContractId = contract.Id };
            _db.Add(plan);
        }
        if (plan.Status != ProjectMaterialPlanStatuses.Draft) return; // المعتمدة أو المقدَّمة لا تُمس (تنبيه في القراءة)
        var lines = contract.Items.ToDictionary(i => i.Id);
        foreach (var old in plan.Lines.Where(l => l.Source == ProjectMaterialSources.Derived).ToList()) { plan.Lines.Remove(old); _db.Remove(old); }
        foreach (var m in plan.Lines)
        {
            var key = m.SubItemId ?? m.BoqItemId;
            if (key is Guid k && phaseOfLine.TryGetValue(k, out var ph)) m.PhaseId = ph;
        }
        var materialRows = costRows.Where(r => r.ResourceType == BoqResourceTypes.Material).ToList();
        var ids = materialRows.Select(r => r.ResourceId).Distinct().ToList();
        var products = await _db.Set<ConstructionResource>().AsNoTracking().Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.ProductId, ct);
        var order = plan.Lines.Count == 0 ? 0 : plan.Lines.Max(l => l.SortOrder);
        foreach (var r in materialRows)
        {
            if (!lines.TryGetValue(r.ContractItemId, out var line)) continue;
            plan.Lines.Add(new ProjectMaterialRequirement
            {
                ContractId = contract.Id, BoqItemId = line.IsSubItem ? line.ParentItemId : line.Id, SubItemId = line.IsSubItem ? line.Id : null,
                ResourceId = r.ResourceId, ProductId = products.GetValueOrDefault(r.ResourceId), Unit = r.Unit, BaselineQuantity = Math.Round(r.Quantity, Decimals),
                Source = ProjectMaterialSources.Derived, SortOrder = ++order, PhaseId = phaseOfLine.TryGetValue(line.Id, out var ph) ? ph : null,
                PlannedUnitCost = r.UnitCost, QuantityPerUnit = r.QuantityPerUnit,
            });
        }
    }

    /// <summary>العمالة والمعدات وغيرها من التحليل المجمّد (المشتقة تُستبدل، واليدوية والمعدّلة تبقى).</summary>
    private async Task GenerateResourcesAsync(CommercialContract contract, ConstructionProjectProfile profile, List<ContractItemResourceCost> costRows,
        Dictionary<Guid, Guid> phaseOfLine, CancellationToken ct)
    {
        var existing = await _db.Set<ProjectResourceRequirement>().Where(r => r.ContractId == contract.Id).ToListAsync(ct);
        _db.RemoveRange(existing.Where(r => r.Source == Src.Derived));
        foreach (var r in existing.Where(r => r.Source != Src.Derived && r.ContractItemId is Guid line && phaseOfLine.ContainsKey(line)))
            r.PhaseId = phaseOfLine[r.ContractItemId!.Value];

        // مورد البند الذي عدّله المدير (أو أضافه) يبقى ولا يُولَّد بجانبه
        var keptPairs = existing.Where(r => r.Source != Src.Derived && r.ContractItemId != null && r.ResourceId != null)
            .Select(r => (r.ContractItemId!.Value, r.ResourceId!.Value)).ToHashSet();
        var rows = costRows.Where(r => r.ResourceType != BoqResourceTypes.Material && !keptPairs.Contains((r.ContractItemId, r.ResourceId))).ToList();
        var ids = rows.Select(r => r.ResourceId).Distinct().ToList();
        var library = await _db.Set<ConstructionResource>().AsNoTracking().Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        var phases = _db.ChangeTracker.Entries<ProjectPhase>().Where(e => e.State != EntityState.Deleted).Select(e => e.Entity)
            .Where(p => p.ContractId == contract.Id).ToDictionary(p => p.Id);
        var dailyHours = ConstructionProjectService.HourSlots(profile.WorkdayStartMinutes, profile.WorkdayEndMinutes, profile.BreakStartMinutes, profile.BreakEndMinutes).Count;
        var order = existing.Where(r => r.Source != Src.Derived).Select(r => r.SortOrder).DefaultIfEmpty(0).Max();
        foreach (var r in rows)
        {
            library.TryGetValue(r.ResourceId, out var res);
            var phaseId = phaseOfLine.TryGetValue(r.ContractItemId, out var ph) ? ph : (Guid?)null;
            var hours = Hours(r);
            var req = new ProjectResourceRequirement
            {
                // التكلفة اليدوية لبند فرعي (ContractCostRowSources.SubItemManual): احتياج «أخرى» ومعرّفه مفتاح البند الفرعي الثابت
                // كما في سطر التحليل، فيبقى تعديل المدير عند إعادة التوليد بلا تكرار
                ContractId = contract.Id, PhaseId = phaseId, ContractItemId = r.ContractItemId, ResourceId = r.ResourceId, ResourceType = r.ResourceType,
                Name = r.ResourceName, Category = res?.Category, Unit = r.Unit, Quantity = r.Quantity, HoursPerUnit = r.HoursPerUnit, PlannedHours = hours,
                UnitCost = r.UnitCost, PlannedCost = r.TotalCost, Source = Src.Derived, SourceCostRowId = r.Id, SortOrder = ++order,
                Ownership = r.ResourceType == BoqResourceTypes.Equipment ? res?.EquipmentOwnership ?? "undecided" : null,
            };
            // العدد المقترح = الساعات ÷ (أيام المرحلة × ساعات اليوم)، حين تُعرف أيام المرحلة
            if (hours > 0 && dailyHours > 0 && phaseId is Guid pid && phases.TryGetValue(pid, out var phase))
            {
                var phaseDays = (phase.PlannedEnd.Date - phase.PlannedStart.Date).Days + 1;
                if (phaseDays > 0) req.SuggestedCount = Math.Ceiling(hours / (phaseDays * dailyHours) * 10) / 10;
            }
            _db.Add(req);
        }
    }

    // ---------- تعديل العمالة والمعدات ----------

    public async Task<ExecutionPlanDto> SaveResourcesAsync(Guid contractId, SaveResourceRequirementsDto request, CancellationToken ct = default)
    {
        await _tx.RunAsync(async token =>
        {
            var contract = await ConstructionProjectService.LoadBoqContractAsync(_db, contractId, token);
            var plan = await _db.Set<ProjectExecutionPlan>().FirstOrDefaultAsync(p => p.ContractId == contractId, token);
            EnsureEditable(contract, plan);
            var incoming = request.Items ?? new();
            var errors = new List<string>();
            var phaseIds = (await _db.Set<ProjectPhase>().AsNoTracking().Where(p => p.ContractId == contractId).Select(p => p.Id).ToListAsync(token)).ToHashSet();
            var lineIds = contract.Items.Select(i => i.Id).ToHashSet();
            foreach (var d in incoming)
            {
                if (string.IsNullOrWhiteSpace(d.Name)) errors.Add(Messages.ExecutionPlanResourceNameRequired);
                if (!BoqResourceTypes.IsValid(d.ResourceType) || d.ResourceType == BoqResourceTypes.Material) errors.Add(Messages.ResourceTypeInvalid);
                if (d.Quantity < 0 || d.UnitCost < 0 || d.HoursPerUnit < 0 || d.SuggestedCount < 0) errors.Add(Messages.ResourceCostNegative);
                if (d.PhaseId is Guid p && !phaseIds.Contains(p)) errors.Add(Messages.ExecutionPlanPhaseInvalid);
                if (d.ContractItemId is Guid l && !lineIds.Contains(l)) errors.Add(Messages.ExecutionPlanPhaseInvalid);
                if (d.Ownership != null && d.Ownership is not ("owned" or "rented" or "undecided")) errors.Add(Messages.ResourceOwnershipInvalid);
            }
            if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors.Distinct().ToList());

            var existing = await _db.Set<ProjectResourceRequirement>().Where(r => r.ContractId == contractId).ToListAsync(token);
            var keep = incoming.Where(d => d.Id.HasValue).Select(d => d.Id!.Value).ToHashSet();
            _db.RemoveRange(existing.Where(e => !keep.Contains(e.Id)));
            var order = 0;
            foreach (var d in incoming)
            {
                var e = d.Id is Guid id ? existing.FirstOrDefault(x => x.Id == id) : null;
                var isNew = e == null;
                e ??= new ProjectResourceRequirement { ContractId = contractId, Source = Src.Manual };
                // السطر المشتق من الدراسة: كميته وساعاته وتكلفته واسمه منها (تُعدَّل في الدراسة)؛ المدير يقرر المرحلة والعدد والملكية
                if (!isNew && e.Source != Src.Manual)
                {
                    d.Quantity = e.Quantity; d.UnitCost = e.UnitCost; d.HoursPerUnit = e.HoursPerUnit; d.Name = e.Name; d.Unit = e.Unit;
                    d.ResourceType = e.ResourceType; d.Category = e.Category;
                }
                var qty = Math.Round(d.Quantity, Decimals);
                var unitCost = Math.Round(d.UnitCost, Decimals);
                var changed = !isNew && (e.Quantity != qty || e.UnitCost != unitCost || e.PhaseId != d.PhaseId || e.HoursPerUnit != d.HoursPerUnit
                    || e.SuggestedCount != d.SuggestedCount || e.Ownership != d.Ownership || e.Name != d.Name.Trim());
                if (changed && e.Source == Src.Derived) e.Source = Src.Modified;
                e.PhaseId = d.PhaseId; e.ContractItemId = isNew ? d.ContractItemId : e.ContractItemId; e.ResourceId = isNew ? d.ResourceId : e.ResourceId;
                e.ResourceType = d.ResourceType; e.Name = d.Name.Trim()[..Math.Min(d.Name.Trim().Length, 200)]; e.Category = Trim(d.Category, 100);
                e.Unit = Trim(d.Unit, 20) ?? string.Empty; e.Quantity = qty; e.HoursPerUnit = d.HoursPerUnit; e.UnitCost = unitCost;
                e.PlannedHours = e.ResourceType is BoqResourceTypes.Labor or BoqResourceTypes.Equipment ? Math.Round(qty * d.HoursPerUnit, 2) : 0;
                e.PlannedCost = DocumentPricing.Round(qty * unitCost);
                e.Ownership = e.ResourceType == BoqResourceTypes.Equipment ? d.Ownership ?? "undecided" : null;
                e.SuggestedCount = Math.Round(d.SuggestedCount, 2); e.Notes = Trim(d.Notes, 500); e.SortOrder = ++order;
                if (isNew) _db.Add(e);
            }
            await _db.SaveChangesAsync(token);
        }, ct);
        return await GetAsync(contractId, ct);
    }

    // ---------- الدورة ----------

    public Task<ExecutionPlanDto> SubmitAsync(Guid contractId, CancellationToken ct = default)
        => ChangeAsync(contractId, St.Draft, async (contract, plan, token) =>
        {
            var phases = await _db.Set<ProjectPhase>().AsNoTracking().Where(p => p.ContractId == contractId).ToListAsync(token);
            if (phases.Count == 0) throw new ValidationFailedException(Messages.ExecutionPlanNoPhases);
            plan.Status = St.Submitted; plan.ReturnNote = null; plan.SubmittedAt = DateTime.UtcNow; plan.SubmittedBy = _user.Name;
        }, ct);

    /// <summary>
    /// الاعتماد: نسختا الدراسة وأساس الخطة مجمّدتان، واحتياجات المواد تُعتمد معها (خطوة واحدة)، والدراسة تصير «خطة معتمدة».
    /// </summary>
    public async Task<ExecutionPlanDto> ApproveAsync(Guid contractId, CancellationToken ct = default)
    {
        await _tx.RunAsync(async token =>
        {
            var contract = await ConstructionProjectService.LoadBoqContractAsync(_db, contractId, token, track: true);
            var plan = await _db.Set<ProjectExecutionPlan>().FirstOrDefaultAsync(p => p.ContractId == contractId, token)
                ?? throw new NotFoundException(Messages.ExecutionPlanNotFound);
            if (plan.Status != St.Submitted) throw new ConflictException(Messages.ExecutionPlanWrongStatus);

            var materialPlan = await _db.Set<ProjectMaterialPlan>().Include(p => p.Lines).FirstOrDefaultAsync(p => p.ContractId == contractId, token);
            if (materialPlan != null && materialPlan.Status != ProjectMaterialPlanStatuses.Approved && materialPlan.Lines.Count > 0)
            {
                if (materialPlan.Lines.Any(l => l.ResourceId == null)) throw new ValidationFailedException(Messages.ProjectMaterialLegacyLines);
                materialPlan.Status = ProjectMaterialPlanStatuses.Approved; materialPlan.ReturnNote = null;
                materialPlan.SubmittedAt ??= DateTime.UtcNow; materialPlan.SubmittedBy ??= _user.Name ?? "system";
                materialPlan.ApprovedAt = DateTime.UtcNow; materialPlan.ApprovedBy = _user.Name ?? "system"; materialPlan.ApprovedByUserId = _user.UserId;
            }
            plan.Status = St.Approved; plan.ApprovedAt = DateTime.UtcNow; plan.ApprovedBy = _user.Name; plan.ApprovedByUserId = _user.UserId;
            if (FeasibilityStatuses.IsPreContract(contract.FeasibilityStatus)) contract.FeasibilityStatus = FeasibilityStatuses.PlanApproved;
            await _db.SaveChangesAsync(token);

            // النسختان المجمّدتان بعد الحفظ (من القراءة نفسها التي يراها المستخدم)
            var view = await GetAsync(contractId, token);
            var analysis = await _analysis.GetAsync(contractId, token);
            var now = DateTime.UtcNow;
            if (!await _db.Set<ProjectSnapshot>().AnyAsync(s => s.ContractId == contractId && s.Kind == ProjectSnapshotKinds.Feasibility && s.Revision == plan.Revision, token))
                _db.Add(new ProjectSnapshot
                {
                    ContractId = contractId, Kind = ProjectSnapshotKinds.Feasibility, Revision = plan.Revision, TakenAt = now, TakenBy = _user.Name, TakenByUserId = _user.UserId,
                    TotalCost = contract.EstimatedCost, ContractValue = contract.ContractValue,
                    Json = JsonSerializer.Serialize(new
                    {
                        contract.FeasibilityNumber, contract.Title, contract.ProjectName, contract.ContractValue, contract.EstimatedCost, contract.DiscountPercent,
                        contract.OverheadPercent, contract.RiskPercent, contract.ProfitPercent, contract.StartDate, contract.EndDate,
                        Items = contract.Items.OrderBy(i => i.SortOrder).Select(i => new
                        {
                            i.Id, i.LineCode, i.SectionName, i.Description, i.Unit, i.Quantity, i.UnitPrice, i.TenderUnitPrice, i.EstimatedUnitCost, i.IsSubItem, i.IsGroup,
                            i.WorkItemId, i.WorkSubItemId,
                        }),
                        Analysis = analysis,
                    }, Json),
                });
            var baseline = new ProjectSnapshot
            {
                ContractId = contractId, Kind = ProjectSnapshotKinds.PlanBaseline, Revision = plan.Revision, TakenAt = now, TakenBy = _user.Name, TakenByUserId = _user.UserId,
                TotalCost = view.Budget.PlannedCost, ContractValue = contract.ContractValue,
                Json = JsonSerializer.Serialize(new { view.Revision, view.Budget, view.Phases, view.Materials, view.MaterialTotals, view.Resources }, Json),
            };
            _db.Add(baseline);
            plan.BaselineSnapshotId = baseline.Id == Guid.Empty ? (baseline.Id = Guid.NewGuid()) : baseline.Id;
            await _db.SaveChangesAsync(token);
        }, ct);
        return await GetAsync(contractId, ct);
    }

    public Task<ExecutionPlanDto> ReturnAsync(Guid contractId, ReturnExecutionPlanDto request, CancellationToken ct = default)
    {
        var note = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(note)) throw new ValidationFailedException(Messages.ExecutionPlanReturnReasonRequired);
        return ChangeAsync(contractId, St.Submitted, (contract, plan, token) =>
        {
            plan.Status = St.Draft; plan.ReturnNote = note[..Math.Min(note.Length, 500)]; plan.SubmittedAt = null; plan.SubmittedBy = null;
            return Task.CompletedTask;
        }, ct);
    }

    /// <summary>
    /// مراجعة بعد الاعتماد: مسودة برقم أعلى (النسخ المعتمدة تبقى). قبل العقد تعود الدراسة «دراسة» واحتياجات المواد مسودة معها؛
    /// بعده تبقى المواد المعتمدة (تغييرها بمسار الاحتياجات) والقيمة والكميات بأوامر التغيير.
    /// </summary>
    public Task<ExecutionPlanDto> ReviseAsync(Guid contractId, CancellationToken ct = default)
        => ChangeAsync(contractId, St.Approved, async (contract, plan, token) =>
        {
            plan.Status = St.Draft; plan.Revision++; plan.SubmittedAt = null; plan.SubmittedBy = null; plan.ApprovedAt = null;
            plan.ApprovedBy = null; plan.ApprovedByUserId = null; plan.ReturnNote = null;
            if (contract.FeasibilityStatus == FeasibilityStatuses.PlanApproved)
            {
                contract.FeasibilityStatus = FeasibilityStatuses.Study;
                var materialPlan = await _db.Set<ProjectMaterialPlan>().FirstOrDefaultAsync(p => p.ContractId == contractId, token);
                if (materialPlan?.Status == ProjectMaterialPlanStatuses.Approved
                    && !await _db.Set<MaterialRequisition>().AnyAsync(r => r.ContractId == contractId, token))
                {
                    materialPlan.Status = ProjectMaterialPlanStatuses.Draft; materialPlan.ApprovedAt = null; materialPlan.ApprovedBy = null; materialPlan.ApprovedByUserId = null;
                }
            }
        }, ct);

    private async Task<ExecutionPlanDto> ChangeAsync(Guid contractId, string from, Func<CommercialContract, ProjectExecutionPlan, CancellationToken, Task> apply, CancellationToken ct)
    {
        await _tx.RunAsync(async token =>
        {
            var contract = await ConstructionProjectService.LoadBoqContractAsync(_db, contractId, token, track: true);
            ConstructionProjectService.EnsureEditable(contract);
            var plan = await _db.Set<ProjectExecutionPlan>().FirstOrDefaultAsync(p => p.ContractId == contractId, token)
                ?? throw new NotFoundException(Messages.ExecutionPlanNotFound);
            if (plan.Status != from) throw new ConflictException(Messages.ExecutionPlanWrongStatus);
            await apply(contract, plan, token);
            await _db.SaveChangesAsync(token);
        }, ct);
        return await GetAsync(contractId, ct);
    }

    // ---------- المقارنة ----------

    private static readonly string[] Categories = { BoqResourceTypes.Material, BoqResourceTypes.Labor, BoqResourceTypes.Equipment, "other" };

    private static string CategoryOf(string resourceType)
        => resourceType is BoqResourceTypes.Material or BoqResourceTypes.Labor or BoqResourceTypes.Equipment ? resourceType : "other";

    /// <summary>
    /// الدراسة من أول نسخة مجمّدة (تحليل التكلفة لكل نوع)، والأساس من أول أساس معتمد، والحالي من الخطة، والفعلي: المواد من أذونات
    /// التسليم المرحّلة ناقص المرتجع المرحّل (الهدر داخل المسلَّم)، والعمالة والمعدات من موارد التقارير اليومية المعتمدة بتكلفة ساعتها
    /// المثبّتة. المرحلة الفعلية من بند جدول الكميات؛ ما بلا بند «غير موزّع».
    /// </summary>
    public async Task<ExecutionPlanComparisonDto> ComparisonAsync(Guid contractId, CancellationToken ct = default)
    {
        var current = await GetAsync(contractId, ct);
        var snapshots = await _db.Set<ProjectSnapshot>().AsNoTracking().Where(s => s.ContractId == contractId).OrderBy(s => s.Revision).ToListAsync(ct);
        var study = snapshots.FirstOrDefault(s => s.Kind == ProjectSnapshotKinds.Feasibility);
        var baseline = snapshots.FirstOrDefault(s => s.Kind == ProjectSnapshotKinds.PlanBaseline);

        var studyByType = new Dictionary<string, decimal>();
        if (study != null)
            using (var doc = JsonDocument.Parse(study.Json))
                if (doc.RootElement.TryGetProperty("analysis", out var a) && a.TryGetProperty("totalsByType", out var totals))
                    foreach (var t in totals.EnumerateArray())
                    {
                        var k = CategoryOf(t.GetProperty("resourceType").GetString() ?? "other");
                        studyByType[k] = studyByType.GetValueOrDefault(k) + t.GetProperty("total").GetDecimal();
                    }
        ExecutionPlanDto? base1 = baseline == null ? null : JsonSerializer.Deserialize<ExecutionPlanDto>(baseline.Json, Json);

        // الفعلي
        var phaseOfLine = current.Phases.SelectMany(p => p.ContractItemIds.Select(i => (i, p.Id))).GroupBy(x => x.i).ToDictionary(g => g.Key, g => g.First().Id);
        var issues = await _db.Set<ProjectMaterialIssue>().AsNoTracking().Where(i => i.ContractId == contractId && i.Status == ProjectMaterialIssueStatuses.Posted
                && (i.Kind == ProjectMaterialIssueKinds.Issue || i.Kind == ProjectMaterialIssueKinds.Return))
            .SelectMany(i => i.Lines.Select(l => new { i.Kind, l.BoqItemId, l.TotalCost })).ToListAsync(ct);
        var usage = await _db.Set<SiteResourceUsage>().AsNoTracking()
            .Where(u => u.Report.ContractId == contractId && u.Report.Status == SiteDailyReportStatuses.Approved)
            .Join(_db.Set<ProjectResource>().AsNoTracking(), u => u.ProjectResourceId, r => r.Id, (u, r) => new { u.ContractItemId, u.Cost, Hours = u.Hours + u.OvertimeHours, r.Kind })
            .ToListAsync(ct);
        var actual = new List<(string Category, Guid? Phase, decimal Cost, decimal Hours)>();
        foreach (var l in issues)
            actual.Add((BoqResourceTypes.Material, l.BoqItemId is Guid b && phaseOfLine.TryGetValue(b, out var ph) ? ph : null,
                l.Kind == ProjectMaterialIssueKinds.Return ? -l.TotalCost : l.TotalCost, 0));
        foreach (var u in usage)
            actual.Add((ProjectResourceKinds.IsEquipment(u.Kind) ? BoqResourceTypes.Equipment : BoqResourceTypes.Labor,
                u.ContractItemId is Guid c && phaseOfLine.TryGetValue(c, out var ph) ? ph : null, u.Cost, u.Hours));

        decimal CurrentOf(ExecutionPlanBudgetDto b, string k) => k switch
        {
            BoqResourceTypes.Material => b.MaterialsCost, BoqResourceTypes.Labor => b.LaborCost, BoqResourceTypes.Equipment => b.EquipmentCost, _ => b.OtherCost,
        };
        decimal HoursOf(ExecutionPlanBudgetDto b, string k) => k == BoqResourceTypes.Labor ? b.LaborHours : k == BoqResourceTypes.Equipment ? b.EquipmentHours : 0;

        var dto = new ExecutionPlanComparisonDto { ContractId = contractId, BaselineRevision = baseline?.Revision, CurrentRevision = current.Revision };
        foreach (var k in Categories)
            dto.Categories.Add(Row(k, null, studyByType.GetValueOrDefault(k), base1 == null ? 0 : CurrentOf(base1.Budget, k), CurrentOf(current.Budget, k),
                actual.Where(a => a.Category == k).Sum(a => a.Cost), base1 == null ? 0 : HoursOf(base1.Budget, k), HoursOf(current.Budget, k),
                actual.Where(a => a.Category == k).Sum(a => a.Hours)));
        foreach (var p in current.Phases)
        {
            var bp = base1?.Phases.FirstOrDefault(x => x.Id == p.Id);
            var ap = actual.Where(a => a.Phase == p.Id).ToList();
            var currentPhaseCost = current.Materials.Where(m => m.PhaseId == p.Id).Sum(m => m.PlannedCost) + current.Resources.Where(r => r.PhaseId == p.Id).Sum(r => r.PlannedCost);
            var basePhaseCost = base1 == null || bp == null ? 0
                : base1.Materials.Where(m => m.PhaseId == p.Id).Sum(m => m.PlannedCost) + base1.Resources.Where(r => r.PhaseId == p.Id).Sum(r => r.PlannedCost);
            dto.Phases.Add(Row(p.Id.ToString(), p.Name, p.PlannedCost, basePhaseCost, currentPhaseCost, ap.Sum(a => a.Cost), bp?.Hours ?? 0, p.Hours, ap.Sum(a => a.Hours)));
        }
        var unallocated = actual.Where(a => a.Phase == null).ToList();
        if (unallocated.Count > 0) dto.Phases.Add(Row("unallocated", null, 0, 0, 0, unallocated.Sum(a => a.Cost), 0, 0, unallocated.Sum(a => a.Hours)));
        dto.Total = Row("total", null, dto.Categories.Sum(c => c.Study), dto.Categories.Sum(c => c.Baseline), dto.Categories.Sum(c => c.Current),
            dto.Categories.Sum(c => c.Actual), dto.Categories.Sum(c => c.BaselineHours), dto.Categories.Sum(c => c.CurrentHours), dto.Categories.Sum(c => c.ActualHours));
        return dto;
    }

    private static PlanComparisonRowDto Row(string key, string? name, decimal study, decimal baseline, decimal current, decimal actual,
        decimal baselineHours, decimal currentHours, decimal actualHours) => new()
    {
        Key = key, Name = name, Study = DocumentPricing.Round(study), Baseline = DocumentPricing.Round(baseline), Current = DocumentPricing.Round(current),
        Actual = DocumentPricing.Round(actual), BaselineHours = Math.Round(baselineHours, 2), CurrentHours = Math.Round(currentHours, 2),
        ActualHours = Math.Round(actualHours, 2), Variance = DocumentPricing.Round(actual - current),
        ConsumedPercent = current > 0 ? Math.Round(actual / current * 100m, 1) : 0,
    };

    // ---------- الحالة ----------

    /// <summary>عناصر الخطة تُولَّد وتُعدَّل في المسودة (أو قبل أي خطة)، والعقد غير مغلق ولا ملغى.</summary>
    internal static bool IsEditable(CommercialContract c, ProjectExecutionPlan? plan)
        => c.Stage is not (ContractStageDefinition.Closed or ContractStageDefinition.Cancelled) && (plan == null || plan.Status == St.Draft);

    private static void EnsureEditable(CommercialContract c, ProjectExecutionPlan? plan)
    {
        ConstructionProjectService.EnsureEditable(c);
        if (!IsEditable(c, plan)) throw new ConflictException(Messages.ExecutionPlanLocked);
    }

    /// <summary>للتحقق من خارج الخدمة (المراحل): خطة مقدَّمة أو معتمدة لا تُعدَّل عناصرها.</summary>
    internal static async Task EnsurePlanEditableAsync(ErpDbContext db, Guid contractId, CancellationToken ct)
    {
        var status = await db.Set<ProjectExecutionPlan>().AsNoTracking().Where(p => p.ContractId == contractId).Select(p => p.Status).FirstOrDefaultAsync(ct);
        if (status is St.Submitted or St.Approved) throw new ConflictException(Messages.ExecutionPlanLocked);
    }

    /// <summary>
    /// قيد بدء التنفيذ (التقرير اليومي، أذونات الصرف، طلبات شراء المشروع) لسجل جاء من دراسة الجدوى: عقد سارٍ (أو في الاستلام
    /// الابتدائي) وخطة تنفيذ معتمدة. العقود المباشرة والقديمة (بلا مرحلة دراسة) على قواعدها كما كانت.
    /// </summary>
    internal static async Task EnsureExecutionAllowedAsync(ErpDbContext db, Guid contractId, CancellationToken ct)
    {
        var c = await db.Set<CommercialContract>().AsNoTracking().Where(x => x.Id == contractId)
            .Select(x => new { x.FeasibilityStatus, x.Stage }).FirstOrDefaultAsync(ct);
        if (c?.FeasibilityStatus == null) return;
        if (FeasibilityStatuses.IsPreContract(c.FeasibilityStatus)) throw new ConflictException(Messages.ExecutionNeedsContract);
        if (c.Stage is not (ContractStageDefinition.Active or ContractStageDefinition.InitialAcceptance))
            throw new ConflictException(Messages.ExecutionNeedsActiveContract);
        var status = await db.Set<ProjectExecutionPlan>().AsNoTracking().Where(p => p.ContractId == contractId).Select(p => p.Status).FirstOrDefaultAsync(ct);
        if (status != St.Approved) throw new ConflictException(Messages.ExecutionNeedsApprovedPlan);
    }

    private static decimal Hours(ContractItemResourceCost r)
        => r.ResourceType is BoqResourceTypes.Labor or BoqResourceTypes.Equipment ? Math.Round(r.Quantity * r.HoursPerUnit, 2) : 0;

    private static List<Guid> ParseIds(string? csv)
        => string.IsNullOrWhiteSpace(csv) ? new() : csv.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList();

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? Trim(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : s.Trim() is var t && t.Length > max ? t[..max] : s.Trim();

    private static ProjectResourceRequirementDto ToDto(ProjectResourceRequirement r) => new()
    {
        Id = r.Id, PhaseId = r.PhaseId, ContractItemId = r.ContractItemId, ResourceId = r.ResourceId, ResourceType = r.ResourceType, Name = r.Name,
        Category = r.Category, Unit = r.Unit, Quantity = r.Quantity, HoursPerUnit = r.HoursPerUnit, PlannedHours = r.PlannedHours, UnitCost = r.UnitCost,
        PlannedCost = r.PlannedCost, Ownership = r.Ownership, SuggestedCount = r.SuggestedCount, Source = r.Source, Notes = r.Notes,
    };
}
