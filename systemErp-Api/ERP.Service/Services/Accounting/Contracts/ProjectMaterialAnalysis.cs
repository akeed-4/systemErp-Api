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
        /// <summary>
        /// كل موارد السطر بعد فك المركّب (المواد والعمالة والمعدات ومقاولو الباطن وغيرها) بكمياتها ومنشئها: مصدر تحليل التكلفة
        /// والاحتياجات غير المادية. المواد هنا هي نفسها في <see cref="Materials"/>.
        /// </summary>
        public List<Material> Resources { get; } = new();
        /// <summary>
        /// بنود فرعية في المكتبة بلا احتياجات وبتكلفة وحدة يدوية: تدخل تكلفة السطر كما تدخل تكلفة البند في المكتبة،
        /// وليست موارد (لا مواد ولا ساعات).
        /// </summary>
        public List<ManualCost> ManualCosts { get; } = new();
    }

    /// <summary>تكلفة يدوية لبند فرعي في المكتبة بكميته على السطر.</summary>
    public sealed record ManualCost(Guid SubItemId, string Description, string Unit, decimal Quantity, decimal UnitCost);

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
    /// تحليل سطر:
    /// <list type="bullet">
    /// <item>البند بلا فروع في العقد: احتياجاته المباشرة واحتياجات كل فروعه في المكتبة بكمياتها.</item>
    /// <item>البند الفرعي: احتياجات بنده الفرعي في المكتبة بكميته. وسطر «الأعمال المباشرة» (فرعي بلا بند فرعي في المكتبة ومرتبط
    /// ببند المكتبة نفسه للرئيسي) يحمل الاحتياجات المباشرة للبند الرئيسي بكميته.</item>
    /// <item>البند المجموعة لا يُنفَّذ ولا يُسعَّر: احتياجاته المباشرة تُحمل على سطر أعماله المباشرة، وبدونه تبقى للتشخيص
    /// (<see cref="Dg.GroupDirectUnassigned"/>) فلا تُحتسب مرتين ولا تضيع بصمت.</item>
    /// </list>
    /// </summary>
    public LineResult Analyze(CommercialContractItem line)
    {
        if (line.IsSubItem)
        {
            var parent = line.ParentItemId is Guid pid ? _items.GetValueOrDefault(pid) : null;
            var sub = new LineResult { Line = line, BoqItemId = parent?.Id, SubItemId = line.Id };
            if (parent?.WorkItemId is not Guid parentWork) { sub.SkipReason = Dg.ParentNotLinked; return sub; }
            if (!_works.TryGetValue(parentWork, out var parentItem)) { sub.SkipReason = Dg.WorkItemMissing; return sub; }
            if (IsDirectWork(line, parent))
            {
                AddComponents(sub, parentItem.Components.Where(c => c.SubItemId == null), line.RevisedQuantity, null);
                Close(sub);
                return sub;
            }
            if (line.WorkSubItemId is not Guid librarySub) { sub.SkipReason = Dg.SubItemNoSource; return sub; }
            var source = parentItem.SubItems.FirstOrDefault(s => s.Id == librarySub);
            if (source == null) { sub.SkipReason = Dg.SubItemNoSource; return sub; }
            AddSubItem(sub, parentItem, source, line.RevisedQuantity);
            Close(sub);
            return sub;
        }

        var result = new LineResult { Line = line, BoqItemId = line.Id };
        if (line.WorkItemId is not Guid workId) { result.SkipReason = Dg.NotLinked; return result; }
        if (!_works.TryGetValue(workId, out var work)) { result.SkipReason = Dg.WorkItemMissing; return result; }
        if (line.IsGroup)
        {
            if (HasDirectWorkLine(line)) { result.SkipReason = Dg.NoMaterials; return result; } // سطر أعماله المباشرة يحملها
            AddComponents(result, work.Components.Where(c => c.SubItemId == null), line.RevisedQuantity, null);
            Close(result);
            if (result.Resources.Count > 0) result.SkipReason = Dg.GroupDirectUnassigned;
            return result;
        }
        AddComponents(result, work.Components.Where(c => c.SubItemId == null), line.RevisedQuantity, null);
        foreach (var s in work.SubItems.OrderBy(s => s.SortOrder))
            AddSubItem(result, work, s, s.QuantityMode == BoqQuantityModes.PerUnit ? line.RevisedQuantity * s.QuantityFactor : s.Quantity);
        Close(result);
        return result;
    }

    /// <summary>
    /// سطر فرعي يحمل الأعمال المباشرة لبنده الرئيسي: بلا بند فرعي في المكتبة، ومرتبط ببند المكتبة نفسه للرئيسي.
    /// </summary>
    public static bool IsDirectWork(CommercialContractItem line, CommercialContractItem? parent)
        => line.IsSubItem && line.WorkSubItemId == null && line.WorkItemId is Guid w && parent?.WorkItemId == w;

    private bool HasDirectWorkLine(CommercialContractItem group)
        => _items.Values.Any(i => i.ParentItemId == group.Id && IsDirectWork(i, group));

    /// <summary>احتياجات البند الفرعي في المكتبة بكميته، أو تكلفته اليدوية إن لم تكن له احتياجات.</summary>
    private void AddSubItem(LineResult result, WorkItem work, WorkItemSubItem s, decimal subQty)
    {
        var components = work.Components.Where(c => c.SubItemId == s.Id).ToList();
        if (components.Count > 0) AddComponents(result, components, subQty, s.Description);
        else if (s.UnitCost > 0 && subQty > 0) result.ManualCosts.Add(new ManualCost(s.Id, s.Description, s.Unit, subQty, s.UnitCost));
    }

    private static void Close(LineResult r)
    {
        r.Resources.RemoveAll(m => m.Quantity <= 0);
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
        var use = result.Resources.FirstOrDefault(m => m.Resource.Id == r.Id);
        if (use == null)
        {
            use = new Material { Resource = r };
            result.Resources.Add(use);
        }
        use.Quantity += qty;
        use.Origins.Add(origin(qty));

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
