using System.Text.Json;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// تحليل تكلفة دراسة الجدوى (جدول كميات عقد المقاولات) من مكتبة التكاليف، مجمّداً على الأسطر (<see cref="ContractItemResourceCost"/>):
/// <list type="bullet">
/// <item>التحليل من <see cref="ProjectMaterialAnalysis"/> نفسه بكل أنواع الموارد، والتكلفة من سجل المكتبة في تاريخ التسعير
/// (<see cref="ResourceCostRules.CostAtAsync"/>).</item>
/// <item>التسعير والتحديث إجراء صريح بمعاينة للفروق؛ تغيّر المكتبة بعده لا يغيّر شيئاً. التكلفة المعدّلة يدوياً تبقى إن طُلب.</item>
/// <item>تكلفة وحدة السطر = Σ (الكمية × التكلفة) ÷ كمية السطر وقت التسعير، وحفظ العقد يأخذها من التحليل لا من الطلب.</item>
/// <item>التعديل في المسودة وحدها؛ بعدها للقراءة.</item>
/// </list>
/// </summary>
public class ContractCostAnalysisService : IContractCostAnalysisService
{
    private const int Decimals = 4;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ErpDbContext _db;
    private readonly ITransactionRunner _tx;
    private readonly ICurrentUser _user;

    public ContractCostAnalysisService(ErpDbContext db, ITransactionRunner tx, ICurrentUser user)
    {
        _db = db; _tx = tx; _user = user;
    }

    // ---------- القراءة ----------

    public async Task<ContractCostAnalysisDto> GetAsync(Guid contractId, CancellationToken ct = default)
    {
        var contract = await LoadContractAsync(contractId, track: false, ct);
        var rows = await _db.Set<ContractItemResourceCost>().AsNoTracking().Where(r => r.ContractId == contractId)
            .OrderBy(r => r.SortOrder).ToListAsync(ct);
        var analysis = await ProjectMaterialAnalysis.LoadAsync(_db, contract, ct);
        var byLine = rows.GroupBy(r => r.ContractItemId).ToDictionary(g => g.Key, g => g.ToList());

        var dto = new ContractCostAnalysisDto
        {
            ContractId = contract.Id, Editable = IsEditable(contract), EstimatedCost = contract.EstimatedCost,
            Currency = await ResourceCostRules.CurrencyAsync(_db, ct),
        };
        foreach (var line in contract.Items.OrderBy(i => i.SortOrder))
        {
            var lineDto = new ContractCostLineDto
            {
                ContractItemId = line.Id, LineCode = line.LineCode, Description = line.Description, Unit = line.Unit, Quantity = line.RevisedQuantity,
                IsSubItem = line.IsSubItem, IsGroup = line.IsGroup, EstimatedUnitCost = line.EstimatedUnitCost,
                EstimatedTotal = DocumentPricing.Round(line.RevisedQuantity * line.EstimatedUnitCost),
            };
            if (line.IsGroup)
            {
                lineDto.Status = "group";
                // احتياجات مباشرة للرئيسي بلا سطر أعمال مباشرة: لا تدخل التكلفة (تنبيه لا يُترك صامتاً)
                if (analysis.Analyze(line).SkipReason == ProjectMaterialDiagnostics.GroupDirectUnassigned)
                    lineDto.SkipReason = ProjectMaterialDiagnostics.GroupDirectUnassigned;
            }
            else if (byLine.TryGetValue(line.Id, out var lineRows))
            {
                lineDto.Status = "priced";
                lineDto.PricingDate = lineRows.Max(r => r.PricingDate);
                lineDto.Resources = lineRows.Select(ToDto).ToList();
                dto.AnalyzedCost += lineDto.EstimatedTotal;
            }
            else
            {
                var skip = SkipReason(analysis.Analyze(line));
                lineDto.Status = skip == null ? "manual" : "skipped";
                lineDto.SkipReason = skip;
            }
            dto.Lines.Add(lineDto);
        }
        dto.TotalsByType = rows.GroupBy(r => r.ResourceType)
            .Select(g => new CostTypeTotalDto { ResourceType = g.Key, Total = g.Sum(r => r.TotalCost), Hours = g.Sum(Hours) })
            .OrderBy(t => Array.IndexOf(BoqResourceTypes.All.ToArray(), t.ResourceType)).ToList();
        dto.AnalyzedCost = DocumentPricing.Round(dto.AnalyzedCost);
        return dto;
    }

    // ---------- المعاينة ----------

    public async Task<ContractCostRefreshPreviewDto> PreviewAsync(Guid contractId, DateTime? pricingDate, CancellationToken ct = default)
    {
        var contract = await LoadContractAsync(contractId, track: false, ct);
        var date = (pricingDate ?? ResourceCostRules.Today).Date;
        var priced = await PriceAsync(contract, date, ct);
        var current = (await _db.Set<ContractItemResourceCost>().AsNoTracking().Where(r => r.ContractId == contractId).ToListAsync(ct))
            .GroupBy(r => r.ContractItemId).ToDictionary(g => g.Key, g => g.ToList());

        var preview = new ContractCostRefreshPreviewDto { PricingDate = date };
        foreach (var p in priced)
        {
            var line = p.Line;
            var currentRows = current.GetValueOrDefault(line.Id) ?? new();
            var item = new ContractCostRefreshLineDto
            {
                ContractItemId = line.Id, LineCode = line.LineCode, Description = line.Description, SkipReason = p.SkipReason,
                CurrentUnitCost = line.EstimatedUnitCost, CurrentTotal = DocumentPricing.Round(line.RevisedQuantity * line.EstimatedUnitCost),
            };
            if (p.SkipReason != null)
            {
                item.Status = "skipped";
                item.NewUnitCost = item.CurrentUnitCost; item.NewTotal = item.CurrentTotal;
            }
            else
            {
                var rows = Merge(p, currentRows, keepOverrides: true);
                item.NewUnitCost = LineUnitCost(rows, line.RevisedQuantity);
                item.NewTotal = DocumentPricing.Round(line.RevisedQuantity * item.NewUnitCost);
                item.Changes = Diff(currentRows, rows);
                item.Status = currentRows.Count == 0 ? "new" : item.Changes.Count > 0 || item.NewUnitCost != item.CurrentUnitCost ? "changed" : "unchanged";
            }
            preview.CurrentTotal += item.CurrentTotal;
            preview.NewTotal += item.NewTotal;
            preview.Lines.Add(item);
        }
        preview.CurrentTotal = DocumentPricing.Round(preview.CurrentTotal);
        preview.NewTotal = DocumentPricing.Round(preview.NewTotal);
        return preview;
    }

    // ---------- التطبيق ----------

    public async Task<ContractCostAnalysisDto> ApplyAsync(Guid contractId, ApplyContractCostRefreshDto request, CancellationToken ct = default)
    {
        await _tx.RunAsync(async token =>
        {
            var contract = await LoadContractAsync(contractId, track: true, token);
            EnsureEditable(contract);
            var date = (request.PricingDate ?? ResourceCostRules.Today).Date;
            var wanted = request.ContractItemIds is { Count: > 0 } ids ? ids.ToHashSet() : null;
            var priced = (await PriceAsync(contract, date, token)).Where(p => p.SkipReason == null && (wanted == null || wanted.Contains(p.Line.Id))).ToList();
            var existing = await _db.Set<ContractItemResourceCost>().Where(r => r.ContractId == contractId).ToListAsync(token);
            var now = DateTime.UtcNow;
            foreach (var p in priced)
            {
                var old = existing.Where(r => r.ContractItemId == p.Line.Id).ToList();
                var rows = Merge(p, old, request.KeepOverrides);
                _db.RemoveRange(old);
                var order = 0;
                foreach (var r in rows)
                {
                    r.ContractId = contractId; r.SortOrder = ++order; r.PricedAt = now; r.PricedBy = _user.Name; r.PricedByUserId = _user.UserId;
                    _db.Add(r);
                }
                p.Line.EstimatedUnitCost = LineUnitCost(rows, p.Line.RevisedQuantity);
            }
            CommercialContractService.RecomputeEstimate(contract);
            await _db.SaveChangesAsync(token);
        }, ct);
        return await GetAsync(contractId, ct);
    }

    public async Task<ContractCostAnalysisDto> OverrideAsync(Guid contractId, Guid rowId, OverrideContractCostDto request, CancellationToken ct = default)
    {
        await _tx.RunAsync(async token =>
        {
            var contract = await LoadContractAsync(contractId, track: true, token);
            EnsureEditable(contract);
            if (request.UnitCost < 0) throw new ValidationFailedException(Messages.ResourceCostNegative);
            var row = await _db.Set<ContractItemResourceCost>().FirstOrDefaultAsync(r => r.Id == rowId && r.ContractId == contractId, token)
                ?? throw new NotFoundException(Messages.ContractCostRowNotFound);
            var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
            if (reason == null) throw new ValidationFailedException(Messages.ContractCostOverrideReasonRequired);
            row.UnitCost = Math.Round(request.UnitCost, Decimals);
            row.TotalCost = DocumentPricing.Round(row.Quantity * row.UnitCost);
            row.CostStatus = row.UnitCost == row.StandardUnitCost ? ContractItemCostStatuses.Standard : ContractItemCostStatuses.Overridden;
            row.OverrideReason = row.CostStatus == ContractItemCostStatuses.Overridden ? reason[..Math.Min(reason.Length, 300)] : null;
            row.PricedAt = DateTime.UtcNow; row.PricedBy = _user.Name; row.PricedByUserId = _user.UserId;

            var line = contract.Items.First(i => i.Id == row.ContractItemId);
            var lineRows = await _db.Set<ContractItemResourceCost>().Where(r => r.ContractItemId == line.Id && r.ContractId == contractId).ToListAsync(token);
            line.EstimatedUnitCost = LineUnitCost(lineRows, line.RevisedQuantity);
            CommercialContractService.RecomputeEstimate(contract);
            await _db.SaveChangesAsync(token);
        }, ct);
        return await GetAsync(contractId, ct);
    }

    // ---------- الحساب ----------

    private sealed record Priced(CommercialContractItem Line, string? SkipReason, List<ContractItemResourceCost> Rows);

    /// <summary>تحليل كل سطر غير مجموعة وتسعير موارده من سجل المكتبة في التاريخ (أسطر جديدة غير محفوظة).</summary>
    private async Task<List<Priced>> PriceAsync(CommercialContract contract, DateTime date, CancellationToken ct)
    {
        var analysis = await ProjectMaterialAnalysis.LoadAsync(_db, contract, ct);
        var results = contract.Items.Where(i => !i.IsGroup).OrderBy(i => i.SortOrder).Select(analysis.Analyze).ToList();
        var ids = results.SelectMany(r => r.Resources).Select(m => m.Resource.Id).Distinct().ToList();
        var costs = await ResourceCostRules.CostAtAsync(_db, ids, date, ct);
        var list = new List<Priced>();
        foreach (var r in results)
        {
            var skip = SkipReason(r);
            if (skip != null) { list.Add(new Priced(r.Line, skip, new())); continue; }
            var rows = r.Resources.Select(m =>
            {
                var cost = costs.GetValueOrDefault(m.Resource.Id);
                var unitCost = cost?.UnitCost ?? m.Resource.StandardCost;
                var qty = Math.Round(m.Quantity, 6);
                return new ContractItemResourceCost
                {
                    ContractItemId = r.Line.Id, WorkItemId = LineWorkItem(r.Line, contract), WorkSubItemId = r.Line.WorkSubItemId,
                    ResourceId = m.Resource.Id, ResourceType = m.Resource.ResourceType, ResourceCode = m.Resource.Code, ResourceName = m.Resource.NameAr,
                    Unit = m.Resource.Unit, HoursPerUnit = m.Resource.HoursPerUnit, BaseQuantity = r.Line.RevisedQuantity, Quantity = qty,
                    QuantityPerUnit = r.Line.RevisedQuantity > 0 ? Math.Round(qty / r.Line.RevisedQuantity, 6) : 0,
                    StandardUnitCost = unitCost, UnitCost = unitCost, TotalCost = DocumentPricing.Round(qty * unitCost),
                    CostId = cost?.Id, CostEffectiveFrom = cost?.EffectiveFrom, CostSource = cost?.Source ?? m.Resource.CostSource,
                    CostStatus = ContractItemCostStatuses.Standard, PricingDate = date,
                    InputsJson = JsonSerializer.Serialize(m.Origins.Select(o => new ContractCostInputDto
                    {
                        LibrarySubItem = o.LibrarySubItem, Composite = o.Composite, QuantityMode = o.QuantityMode, ConsumptionFactor = o.ConsumptionFactor,
                        FixedQuantity = o.FixedQuantity, WastePercent = o.WastePercent, OwnerQuantity = o.OwnerQuantity, Quantity = Math.Round(o.Quantity, 6),
                    }), Json),
                };
            }).ToList();
            // التكلفة اليدوية لبند فرعي بلا احتياجات: سطر «أخرى» مجمّد بتكلفته وقت التسعير (ليس مورداً: بلا سجل تكلفة ولا ساعات)
            rows.AddRange(r.ManualCosts.Select(m =>
            {
                var qty = Math.Round(m.Quantity, 6);
                return new ContractItemResourceCost
                {
                    ContractItemId = r.Line.Id, WorkItemId = LineWorkItem(r.Line, contract), WorkSubItemId = r.Line.WorkSubItemId,
                    ResourceId = m.SubItemId, ResourceType = BoqResourceTypes.Other, ResourceCode = string.Empty, ResourceName = m.Description,
                    Unit = m.Unit, HoursPerUnit = 0, BaseQuantity = r.Line.RevisedQuantity, Quantity = qty,
                    QuantityPerUnit = r.Line.RevisedQuantity > 0 ? Math.Round(qty / r.Line.RevisedQuantity, 6) : 0,
                    StandardUnitCost = m.UnitCost, UnitCost = m.UnitCost, TotalCost = DocumentPricing.Round(qty * m.UnitCost),
                    CostSource = ContractCostRowSources.SubItemManual, CostStatus = ContractItemCostStatuses.Standard, PricingDate = date,
                    InputsJson = JsonSerializer.Serialize(new[] { new ContractCostInputDto { LibrarySubItem = m.Description, QuantityMode = BoqQuantityModes.PerUnit, Quantity = qty } }, Json),
                };
            }));
            list.Add(new Priced(r.Line, null, rows));
        }
        return list;
    }

    /// <summary>
    /// أسطر التسعير الجديدة مع القائمة: التكلفة المعدّلة يدوياً لمورد بقي تبقى (إن طُلب)، وتغيّر المعيارية عن السابقة يعلَّم «محدّث».
    /// </summary>
    private static List<ContractItemResourceCost> Merge(Priced p, List<ContractItemResourceCost> old, bool keepOverrides)
    {
        var byResource = old.GroupBy(o => o.ResourceId).ToDictionary(g => g.Key, g => g.First());
        foreach (var r in p.Rows)
        {
            if (!byResource.TryGetValue(r.ResourceId, out var prev)) continue;
            if (keepOverrides && prev.CostStatus == ContractItemCostStatuses.Overridden)
            {
                r.UnitCost = prev.UnitCost;
                r.CostStatus = ContractItemCostStatuses.Overridden;
                r.OverrideReason = prev.OverrideReason;
            }
            else if (r.StandardUnitCost != prev.StandardUnitCost) r.CostStatus = ContractItemCostStatuses.Updated;
            else r.CostStatus = prev.CostStatus == ContractItemCostStatuses.Overridden ? ContractItemCostStatuses.Standard : prev.CostStatus;
            r.TotalCost = DocumentPricing.Round(r.Quantity * r.UnitCost);
        }
        return p.Rows;
    }

    private static List<ContractCostRefreshChangeDto> Diff(List<ContractItemResourceCost> old, List<ContractItemResourceCost> rows)
    {
        var changes = new List<ContractCostRefreshChangeDto>();
        var oldBy = old.GroupBy(o => o.ResourceId).ToDictionary(g => g.Key, g => g.First());
        foreach (var r in rows)
        {
            if (!oldBy.TryGetValue(r.ResourceId, out var o))
            {
                changes.Add(new ContractCostRefreshChangeDto { ResourceId = r.ResourceId, ResourceName = r.ResourceName, ResourceType = r.ResourceType, Kind = "added", NewQuantity = r.Quantity, NewUnitCost = r.UnitCost });
                continue;
            }
            if (o.Quantity != r.Quantity || o.UnitCost != r.UnitCost || o.StandardUnitCost != r.StandardUnitCost)
                changes.Add(new ContractCostRefreshChangeDto
                {
                    ResourceId = r.ResourceId, ResourceName = r.ResourceName, ResourceType = r.ResourceType, Kind = "changed", OldQuantity = o.Quantity,
                    NewQuantity = r.Quantity, OldUnitCost = o.UnitCost, NewUnitCost = r.UnitCost, Overridden = r.CostStatus == ContractItemCostStatuses.Overridden,
                });
        }
        foreach (var o in old.Where(o => rows.All(r => r.ResourceId != o.ResourceId)))
            changes.Add(new ContractCostRefreshChangeDto { ResourceId = o.ResourceId, ResourceName = o.ResourceName, ResourceType = o.ResourceType, Kind = "removed", OldQuantity = o.Quantity, OldUnitCost = o.UnitCost });
        return changes;
    }

    /// <summary>تكلفة وحدة السطر من تحليله = Σ الإجمالي ÷ كمية السطر وقت التسعير.</summary>
    internal static decimal LineUnitCost(IReadOnlyCollection<ContractItemResourceCost> rows, decimal lineQuantity)
    {
        var baseQty = rows.FirstOrDefault()?.BaseQuantity is decimal q && q > 0 ? q : lineQuantity;
        return baseQty > 0 ? Math.Round(rows.Sum(r => r.TotalCost) / baseQty, Decimals) : 0;
    }

    /// <summary>سطر قابل للتحليل: مرتبط بالمكتبة وله موارد (ولو بلا مواد) أو تكلفة يدوية لبند فرعي.</summary>
    private static string? SkipReason(ProjectMaterialAnalysis.LineResult r)
        => r.SkipReason == ProjectMaterialDiagnostics.NoMaterials && (r.Resources.Count > 0 || r.ManualCosts.Count > 0) ? null : r.SkipReason;

    private static Guid? LineWorkItem(CommercialContractItem line, CommercialContract contract)
        => line.IsSubItem && line.ParentItemId is Guid pid ? contract.Items.FirstOrDefault(i => i.Id == pid)?.WorkItemId : line.WorkItemId;

    private static decimal Hours(ContractItemResourceCost r)
        => r.ResourceType is BoqResourceTypes.Labor or BoqResourceTypes.Equipment ? Math.Round(r.Quantity * r.HoursPerUnit, 2) : 0;

    private static ContractCostResourceDto ToDto(ContractItemResourceCost r) => new()
    {
        Id = r.Id, ResourceId = r.ResourceId, ResourceType = r.ResourceType, ResourceCode = r.ResourceCode, ResourceName = r.ResourceName, Unit = r.Unit,
        Quantity = r.Quantity, QuantityPerUnit = r.QuantityPerUnit, Hours = Hours(r), StandardUnitCost = r.StandardUnitCost, UnitCost = r.UnitCost,
        TotalCost = r.TotalCost, CostEffectiveFrom = r.CostEffectiveFrom, CostSource = r.CostSource, CostStatus = r.CostStatus, OverrideReason = r.OverrideReason,
        Inputs = string.IsNullOrEmpty(r.InputsJson) ? new() : JsonSerializer.Deserialize<List<ContractCostInputDto>>(r.InputsJson, Json) ?? new(),
    };

    // ---------- الحالة ----------

    /// <summary>التحليل يُسعَّر ويُعدَّل في المسودة وحدها.</summary>
    internal static bool IsEditable(CommercialContract c) => c.Stage == ContractStageDefinition.Draft && c.FeasibilityStatus != FeasibilityStatuses.PlanApproved;

    private static void EnsureEditable(CommercialContract c)
    {
        if (!IsEditable(c)) throw new ConflictException(Messages.ContractCostAnalysisLocked);
    }

    private async Task<CommercialContract> LoadContractAsync(Guid contractId, bool track, CancellationToken ct)
    {
        var q = _db.Set<CommercialContract>().Include(c => c.Items).AsQueryable();
        if (!track) q = q.AsNoTracking();
        var contract = await q.FirstOrDefaultAsync(c => c.Id == contractId, ct) ?? throw new NotFoundException(Messages.ContractNotFound);
        if (!await CommercialContractService.IsBoqContractAsync(_db, contract.ContractType, ct))
            throw new ConflictException(Messages.ProjectMaterialsOnlyForBoq);
        return contract;
    }
}
