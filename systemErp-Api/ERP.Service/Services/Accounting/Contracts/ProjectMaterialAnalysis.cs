using ERP.Service.Data;
using Dg = ERP.Core.Models.Accounting.ProjectMaterialDiagnostics;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// تحليل مواد سطر في جدول الكميات من مكتبة المقاولات: المصدر الواحد للاشتقاق، والمواد المقترحة للإضافة اليدوية، ومواد التقرير
/// اليومي. الكمية = (لكل وحدة: كمية المالك × معامل الاستهلاك، أو الثابتة) × (1 + الهدر)، والمورد المركّب يُفك لمكوناته.
/// الكميات بوحدة مادة المكتبة؛ الربط بالصنف المخزني وتحويل وحدته شأن العرض لا الاشتقاق.
/// </summary>
internal sealed class ProjectMaterialAnalysis
{
    private const int MaxCompositeDepth = 8;

    /// <summary>منشأ الكمية في المكتبة: البند الفرعي، والمركّب الوسيط، والمعامل والهدر، وكمية المالك.</summary>
    public sealed record Origin(string? LibrarySubItem, string? Composite, string QuantityMode, decimal ConsumptionFactor,
        decimal FixedQuantity, decimal WastePercent, decimal OwnerQuantity, decimal Quantity);

    /// <summary>مادة مكتبة بكميتها المجمّعة على السطر ومنشأ كل جزء منها.</summary>
    public sealed class Material
    {
        public required ConstructionResource Resource { get; init; }
        public decimal Quantity { get; set; }
        public List<Origin> Origins { get; } = new();
    }

    /// <summary>نتيجة سطر: مرجع الاحتياج (البند والفرعي)، وسبب التخطي إن وُجد، والمواد بترتيب ظهورها، وعدد الموارد غير المادية.</summary>
    public sealed class LineResult
    {
        public required CommercialContractItem Line { get; init; }
        public Guid? BoqItemId { get; init; }
        public Guid? SubItemId { get; init; }
        /// <summary>رمز من <see cref="ProjectMaterialDiagnostics"/>، أو فارغ إن حُلّل السطر.</summary>
        public string? SkipReason { get; set; }
        public List<Material> Materials { get; } = new();
        public int OtherResourceCount { get; set; }
    }

    private readonly CommercialContract _contract;
    private readonly Dictionary<Guid, CommercialContractItem> _items;
    private readonly Dictionary<Guid, WorkItem> _works;

    public IReadOnlyDictionary<Guid, ConstructionResource> Resources { get; }

    private ProjectMaterialAnalysis(CommercialContract contract, Dictionary<Guid, WorkItem> works, Dictionary<Guid, ConstructionResource> resources)
    {
        _contract = contract;
        _items = contract.Items.ToDictionary(i => i.Id);
        _works = works;
        Resources = resources;
    }

    /// <summary>بنود المكتبة المرتبطة بالعقد وكل موارد المكتبة (للفك والاختيار العام).</summary>
    public static async Task<ProjectMaterialAnalysis> LoadAsync(ErpDbContext db, CommercialContract contract, CancellationToken ct)
    {
        var workIds = contract.Items.Where(i => i.WorkItemId.HasValue).Select(i => i.WorkItemId!.Value).Distinct().ToList();
        var works = workIds.Count == 0
            ? new Dictionary<Guid, WorkItem>()
            : await db.Set<WorkItem>().AsNoTracking().Include(w => w.SubItems).Include(w => w.Components)
                .Where(w => workIds.Contains(w.Id)).AsSplitQuery().ToDictionaryAsync(w => w.Id, ct);
        var resources = await db.Set<ConstructionResource>().AsNoTracking().Include(r => r.Components).ToDictionaryAsync(r => r.Id, ct);
        return new ProjectMaterialAnalysis(contract, works, resources);
    }

    /// <summary>مادة صالحة للاحتياج والاستهلاك: مورد من نوع مادة وليس مركّباً له مكونات.</summary>
    public static bool IsMaterial(ConstructionResource r)
        => r.ResourceType == BoqResourceTypes.Material && !(r.CostSource == ResourceCostSources.Composite && r.Components.Count > 0);

    /// <summary>مواد المكتبة الفعّالة للاختيار العام.</summary>
    public IEnumerable<ConstructionResource> LibraryMaterials()
        => Resources.Values.Where(r => r.IsActive && IsMaterial(r)).OrderBy(r => r.Code).ThenBy(r => r.NameAr);

    public bool HasLibraryLines => _works.Count > 0;

    /// <summary>كل أسطر جدول الكميات بترتيبها.</summary>
    public IEnumerable<LineResult> AnalyzeAll() => _contract.Items.OrderBy(i => i.SortOrder).Select(Analyze);

    /// <summary>
    /// تحليل سطر: البند الفرعي يأخذ مواد بنده الفرعي في المكتبة بكميته، والبند الرئيسي مواده المباشرة، والبند بلا فروع في العقد
    /// يضيف مواد كل فروعه في المكتبة بكمياتها (فروع البند المجموعة في العقد تحمل موادها بنفسها).
    /// </summary>
    public LineResult Analyze(CommercialContractItem line)
    {
        if (line.IsSubItem)
        {
            var parent = line.ParentItemId is Guid pid ? _items.GetValueOrDefault(pid) : null;
            var sub = new LineResult { Line = line, BoqItemId = parent?.Id, SubItemId = line.Id };
            if (parent?.WorkItemId is not Guid parentWork) { sub.SkipReason = Dg.ParentNotLinked; return sub; }
            if (line.WorkSubItemId is not Guid librarySub) { sub.SkipReason = Dg.SubItemNoSource; return sub; }
            if (!_works.TryGetValue(parentWork, out var parentItem)) { sub.SkipReason = Dg.WorkItemMissing; return sub; }
            var subName = parentItem.SubItems.FirstOrDefault(s => s.Id == librarySub)?.Description;
            if (subName == null) { sub.SkipReason = Dg.SubItemNoSource; return sub; }
            AddComponents(sub, parentItem.Components.Where(c => c.SubItemId == librarySub), line.RevisedQuantity, subName);
            Close(sub);
            return sub;
        }

        var result = new LineResult { Line = line, BoqItemId = line.Id };
        if (line.WorkItemId is not Guid workId) { result.SkipReason = Dg.NotLinked; return result; }
        if (!_works.TryGetValue(workId, out var work)) { result.SkipReason = Dg.WorkItemMissing; return result; }
        AddComponents(result, work.Components.Where(c => c.SubItemId == null), line.RevisedQuantity, null);
        if (!line.IsGroup)
            foreach (var s in work.SubItems.OrderBy(s => s.SortOrder))
            {
                var subQty = s.QuantityMode == BoqQuantityModes.PerUnit ? line.RevisedQuantity * s.QuantityFactor : s.Quantity;
                AddComponents(result, work.Components.Where(c => c.SubItemId == s.Id), subQty, s.Description);
            }
        Close(result);
        return result;
    }

    private static void Close(LineResult r)
    {
        r.Materials.RemoveAll(m => m.Quantity <= 0);
        if (r.Materials.Count == 0) r.SkipReason = Dg.NoMaterials;
    }

    private void AddComponents(LineResult result, IEnumerable<WorkItemComponent> components, decimal ownerQty, string? subName)
    {
        foreach (var c in components.OrderBy(c => c.SortOrder))
        {
            var baseQty = c.QuantityMode == BoqQuantityModes.PerUnit ? ownerQty * c.ConsumptionFactor : c.FixedQuantity;
            var qty = baseQty * (1 + c.WastePercent / 100m);
            Expand(result, c.ResourceId, qty, 0, null,
                q => new Origin(subName, null, c.QuantityMode, c.ConsumptionFactor, c.FixedQuantity, c.WastePercent, ownerQty, q));
        }
    }

    /// <summary>يفك المركّب لمكوناته (بعمق محدود)، ويضيف المادة لنتيجة السطر، ويعدّ ما ليس مادة.</summary>
    private void Expand(LineResult result, Guid resourceId, decimal qty, int depth, string? composite, Func<decimal, Origin> origin)
    {
        if (qty <= 0 || depth > MaxCompositeDepth || !Resources.TryGetValue(resourceId, out var r)) return;
        if (r.CostSource == ResourceCostSources.Composite && r.Components.Count > 0)
        {
            foreach (var c in r.Components)
                Expand(result, c.ComponentResourceId, qty * c.Quantity * (1 + c.WastePercent / 100m), depth + 1, composite ?? r.NameAr,
                    q => origin(q) with { Composite = composite ?? r.NameAr, Quantity = q });
            return;
        }
        if (r.ResourceType != BoqResourceTypes.Material) { result.OtherResourceCount++; return; }
        var material = result.Materials.FirstOrDefault(m => m.Resource.Id == r.Id);
        if (material == null)
        {
            material = new Material { Resource = r };
            result.Materials.Add(material);
        }
        material.Quantity += qty;
        material.Origins.Add(origin(qty));
    }
}
