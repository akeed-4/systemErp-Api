using System.Globalization;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Accounting.Sales;
using ERP.Service.Services.Shared;
using CertSt = ERP.Core.Models.Accounting.ContractCertificateStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// تكلفة المشروع وربحيته من القيود المرحّلة (والمعكوسة مع عكسها فتتصافى) على مركز تكلفة المشروع، وهو مركز عقد العميل وعقود الباطن
/// المرتبطة به. الفعلي = حسابات المصروف + رصيد «مواد مشاريع تحت التنفيذ» على المركز (فمواد الإذن تُحتسب مرة واحدة: قبل اعتراف
/// المستخلص في الأصل، وبعده في تكلفة الإيرادات). التصنيف بمصدر القيد ثم بحسابه:
/// <list type="bullet">
/// <item>المواد: أذونات التسليم وتكلفة مواد المستخلص وحساب المواد تحت التنفيذ.</item>
/// <item>الباطن: قيود فواتير عقود الباطن المرتبطة ومستخلصاتها ودفعاتها (بما فيها غراماتنا عليهم دائنة).</item>
/// <item>العمالة: الرواتب ونهاية الخدمة وقيد تحميل العمالة؛ المعدات: الإهلاك وقيد تحميل المعدات؛ وما سواها «أخرى».</item>
/// </list>
/// غرامات العميل وخصوماته الأخرى على مستخلصاتنا تُعرض خصماً من الإيراد لا تكلفة. القراءة وحدها، بلا حسابات مخزنة.
/// <para>المعدات المستأجرة: استحقاق إيجارها من التقارير اليومية وتسويته، وقيود فواتير مؤجريها في موارد المشروع (تتصافى مع الاستحقاق فلا تتكرر). غير المباشرة: قيود التوزيع (<see cref="OverheadSource"/>).
/// الاحتساب المزدوج: رواتب الموظفين وإهلاك الأصول المحمّلين بساعاتهم من التقارير اليومية، إن رُحِّلت على مركز المشروع أيضاً، تُستبعد بسطر تسوية.</para>
/// </summary>
public class ProjectCostService : IProjectCostService
{
    /// <summary>مصدر قيد تحميل العمالة والمعدات من التقارير اليومية المعتمدة.</summary>
    public const string SiteCostSource = "site_report_cost";
    /// <summary>مصدر قيد توزيع التكاليف غير المباشرة على المشاريع.</summary>
    public const string OverheadSource = "project_overhead";
    /// <summary>مصدر سطر التسوية (بلا قيد) الذي يستبعد التكلفة المحمّلة مرتين.</summary>
    public const string DuplicateSource = "duplicate_exclusion";
    private static readonly HashSet<string> IndirectCategories = new() { "overhead" };

    private static readonly HashSet<string> MaterialSources = new() { "project_material_issue", "contract_certificate_cost" };
    private static readonly HashSet<string> LaborSources = new() { "payroll", "end_of_service" };
    private static readonly HashSet<string> EquipmentSources = new() { "fixed_asset_depreciation" };
    private const int TopDocuments = 8;

    private readonly ErpDbContext _db;
    private readonly IProjectMaterialService _materials;

    public ProjectCostService(ErpDbContext db, IProjectMaterialService materials)
    {
        _db = db; _materials = materials;
    }

    public async Task<ProjectCostReportDto> ReportAsync(Guid contractId, CancellationToken ct = default)
    {
        var c = await _db.Set<CommercialContract>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == contractId, ct)
            ?? throw new NotFoundException(Messages.ContractNotFound);
        var budget = await _db.Set<ConstructionProjectProfile>().AsNoTracking().Where(p => p.ContractId == contractId)
            .Select(p => p.EstimatedBudget).FirstOrDefaultAsync(ct);
        // دورة الميزانية: الدراسة تقدير أصلي، والخطة المعتمدة هي ميزانية التنفيذ التي يُقاس عليها الفعلي والتوقع والربح
        var approved = await ApprovedPlanBudgetAsync(_db, contractId, ct);
        var r = new ProjectCostReportDto
        {
            ContractId = c.Id, CostCenterId = c.CostCenterId, ContractValue = c.ContractValue, EstimatedCost = c.EstimatedCost,
            ProjectBudget = budget, ApprovedPlanBudget = approved?.Total, ApprovedPlanRevision = approved?.Revision,
            PlanRevisionPending = approved?.RevisionPending ?? false,
        };
        (r.BudgetAtCompletion, r.BudgetSource) = approved is { Total: > 0 } ap ? (ap.Total, "approved_plan")
            : budget > 0 ? (budget, "project_budget") : (c.EstimatedCost, "study");
        r.PlannedMargin = r.ContractValue - r.BudgetAtCompletion;
        if (r.BudgetAtCompletion <= 0) r.Notes.Add("لا خطة معتمدة ولا ميزانية للمشروع ولا تكلفة تقديرية: التوقع والانحراف بلا أساس.");
        if (r.PlanRevisionPending) r.Notes.Add($"الخطة في مراجعة لم تُعتمد بعد: الحساب على الخطة المعتمدة (المراجعة {approved!.Revision}) حتى اعتماد التعديل.");

        // الإنجاز المعتمد للعميل
        r.CertifiedValue = await _db.Set<ContractCertificate>().AsNoTracking()
            .Where(x => x.ContractId == contractId && (x.Status == CertSt.Approved || x.Status == CertSt.Invoiced)).SumAsync(x => x.CurrentWorkValue, ct);
        r.ProgressPercent = c.ContractValue > 0 ? Math.Round(r.CertifiedValue / c.ContractValue * 100m, 2) : 0;
        r.SiteOperatingCost = await SiteOperatingCostAsync(contractId, ct);
        var unposted = await _db.Set<SiteDailyReport>().AsNoTracking()
            .CountAsync(x => x.ContractId == contractId && x.Status == SiteDailyReportStatuses.Approved && x.CostRevision == 0 && x.Resources.Any(u => u.Cost > 0), ct);
        r.UnpostedSiteReports = unposted;
        if (unposted > 0)
            r.Notes.Add($"{unposted} تقرير يومي معتمد لم تُحمَّل عمالته ومعداته على المشروع (اعتُمد قبل تفعيل التحميل): رحّلها من قسم التقارير اليومية.");

        // المخطط حسب الفئة: المواد من الاحتياجات المعتمدة، والباطن من عقوده، والباقي تنفيذ مباشر (عمالة ومعدات وأخرى) مجتمعاً
        var subs = await _db.Set<CommercialContract>().AsNoTracking()
            .Where(x => x.ParentContractId == contractId && x.Stage != ContractStageDefinition.Cancelled)
            .Select(x => new { x.Id, x.ContractValue }).ToListAsync(ct);
        var plannedSub = subs.Sum(s => s.ContractValue);
        Dictionary<string, decimal?> planned;
        if (approved != null)
        {
            // المخطط لكل فئة من الخطة المعتمدة؛ الباطن من الخطة، وإلا من قيمة عقوده
            planned = approved.ByCategory.ToDictionary(kv => kv.Key, kv => kv.Value > 0 ? (decimal?)kv.Value : null);
            if (planned.GetValueOrDefault("subcontract") == null && plannedSub > 0) planned["subcontract"] = plannedSub;
        }
        else
        {
            // بلا خطة معتمدة (العقود المباشرة والقديمة): المواد من الاحتياجات المعتمدة، والباطن من عقوده، والباقي تنفيذ مجتمع
            decimal? plannedMaterials = null;
            try
            {
                var plan = await _materials.GetAsync(contractId, ct);
                if (plan.Status == ProjectMaterialPlanStatuses.Approved) plannedMaterials = plan.EstimatedCost;
            }
            catch (NotFoundException) { }
            if (plannedMaterials == null) r.Notes.Add("احتياجات المواد غير معتمدة: لا مخطط للمواد.");
            planned = new() { ["materials"] = plannedMaterials, ["subcontract"] = plannedSub > 0 ? plannedSub : null };
            if (r.BudgetAtCompletion > 0) planned["execution"] = Math.Max(0, r.BudgetAtCompletion - (plannedMaterials ?? 0) - plannedSub);
        }

        if (c.CostCenterId is not Guid cc)
        {
            r.MissingCostCenter = true;
            r.Notes.Add("العقد بلا مركز تكلفة: لا قيود تُنسب للمشروع.");
            Finish(r, planned, new());
            return r;
        }
        r.CostCenterCode = await _db.Set<CostCenter>().AsNoTracking().Where(x => x.Id == cc).Select(x => x.Code).FirstOrDefaultAsync(ct);

        var settings = await _db.Set<AccountingSettings>().AsNoTracking().FirstOrDefaultAsync(ct);
        var wip = settings?.ProjectMaterialsWipAccountCode;
        var deductionAccounts = new HashSet<string?> { settings?.ContractPenaltiesExpenseAccountCode, settings?.ContractOtherDeductionsAccountCode };
        var laborAccount = settings?.ProjectLaborCostAccountCode;
        var equipmentAccount = settings?.ProjectEquipmentCostAccountCode;

        // مراجع عقود الباطن: العقد ومستخلصاته وفواتيره (قيود فواتيرها مرجعها الفاتورة)
        var subIds = subs.Select(s => s.Id).ToList();
        var subRefs = new HashSet<Guid>(subIds);
        subRefs.UnionWith(await _db.Set<ContractCertificate>().AsNoTracking().Where(x => subIds.Contains(x.ContractId)).Select(x => x.Id).ToListAsync(ct));
        subRefs.UnionWith(await _db.Set<ContractInvoice>().AsNoTracking().Where(x => subIds.Contains(x.ContractId) && x.InvoiceId != null)
            .Select(x => x.InvoiceId!.Value).ToListAsync(ct));
        subRefs.UnionWith(await _db.Set<ContractInvoice>().AsNoTracking().Where(x => subIds.Contains(x.ContractId) && x.JournalEntryId != null)
            .Select(x => x.JournalEntryId!.Value).ToListAsync(ct));

        var lines = await (from l in _db.Set<JournalEntryLine>().AsNoTracking()
                           join e in _db.Set<JournalEntry>().AsNoTracking() on l.JournalEntryId equals e.Id
                           join a in _db.Set<Account>().AsNoTracking() on l.AccountCode equals a.Code
                           where l.CostCenterId == cc && e.Status != JournalEntryStatus.Draft
                           select new
                           {
                               l.JournalEntryId, e.EntryNumber, e.Date, e.Description, e.SourceType, e.ReferenceNumber,
                               Ref = e.SourceReferenceId ?? e.ReferenceId, l.AccountCode, a.NameAr, a.Type, l.Debit, l.Credit,
                           }).ToListAsync(ct);

        // القيد العكسي (مرجعه القيد الأصلي) يُصنَّف بمصدر قيده الأصلي ومرجعه، فيتصافى معه في فئته نفسها (حذف فاتورة إيجار مثلاً)
        var reversed = lines.Where(l => l.SourceType == "reversal" && l.Ref != null).Select(l => l.Ref!.Value).Distinct().ToList();
        var originals = reversed.Count == 0 ? new Dictionary<Guid, (string? Source, Guid? Ref)>()
            : (await _db.Set<JournalEntry>().AsNoTracking().Where(e => reversed.Contains(e.Id))
                .Select(e => new { e.Id, e.SourceType, Ref = e.SourceReferenceId ?? e.ReferenceId }).ToListAsync(ct))
                .ToDictionary(e => e.Id, e => (e.SourceType, e.Ref));
        (string? Source, Guid? Ref) Origin(string? source, Guid? reference)
            => source == "reversal" && reference is Guid o && originals.TryGetValue(o, out var org) ? org : (source, reference);

        // المعدات المستأجرة: تُصنَّف فقط الفواتير التي تحمل سطراً مرتبطاً بمعدة من هذا المشروع.
        var rentalResourceIds = await _db.Set<ProjectResource>().AsNoTracking()
            .Where(x => x.ContractId == contractId && x.Kind == ProjectResourceKinds.RentedEquipment)
            .Select(x => x.Id).ToListAsync(ct);
        var rentalRatios = new Dictionary<Guid, decimal>();
        if (rentalResourceIds.Count > 0)
        {
            var refs = lines.Select(l => Origin(l.SourceType, l.Ref).Ref).Where(r => r != null).Select(r => r!.Value).Distinct().ToList();
            var rentalInvoices = await _db.Set<PurchaseInvoice>().AsNoTracking().Include(i => i.Items)
                .Where(i => refs.Contains(i.Id) && i.Items.Any(item => item.ProjectResourceId.HasValue
                    && rentalResourceIds.Contains(item.ProjectResourceId.Value)))
                .ToListAsync(ct);
            var rentalReturns = await _db.Set<PurchaseReturn>().AsNoTracking().Include(i => i.Items)
                .Where(i => refs.Contains(i.Id) && i.Items.Any(item => item.ProjectResourceId.HasValue
                    && rentalResourceIds.Contains(item.ProjectResourceId.Value)))
                .ToListAsync(ct);
            foreach (var document in rentalInvoices.Cast<InvoiceBase>().Concat(rentalReturns))
            {
                var ratio = RentalInvoiceClassification.ResourceExpenseRatioAtCenter(document, rentalResourceIds, cc);
                if (ratio > 0) rentalRatios[document.Id] = ratio;
            }
        }

        var costLines = new List<(string Category, Guid? Entry, string? Number, DateTime Date, string? Source, string? RefNo, string Description, string? Account, string? AccountName, decimal Amount)>();
        foreach (var l in lines)
        {
            var isWip = wip != null && l.AccountCode == wip;
            var isSub = subRefs.Contains(l.JournalEntryId) || (l.Ref is Guid rf && subRefs.Contains(rf));
            if (l.Type == AccountCategory.Revenue)
            {
                // إيراد على المركز: غرامات نفرضها على الباطن تخفض تكلفته، وغيره إيراد المشروع
                if (isSub) costLines.Add(("subcontract", l.JournalEntryId, l.EntryNumber, l.Date, l.SourceType, l.ReferenceNumber, l.Description, l.AccountCode, l.NameAr, l.Debit - l.Credit));
                else r.BookedRevenue += l.Credit - l.Debit;
                continue;
            }
            if (l.Type != AccountCategory.Expense && !isWip) continue;
            var amount = l.Debit - l.Credit;
            if (!isWip && deductionAccounts.Contains(l.AccountCode) && !isSub) { r.ClientDeductions += amount; continue; }
            var (source, reference) = Origin(l.SourceType, l.Ref);
            var category = isSub ? "subcontract"
                : isWip || MaterialSources.Contains(source ?? string.Empty) ? "materials"
                : RentalAccrualService.Sources.Contains(source ?? string.Empty) ? "rented_equipment"
                : source == SiteCostSource ? (l.AccountCode == equipmentAccount ? "equipment" : l.AccountCode == laborAccount ? "labor" : "other")
                : source == OverheadSource ? "overhead"
                : LaborSources.Contains(source ?? string.Empty) ? "labor"
                : EquipmentSources.Contains(source ?? string.Empty) ? "equipment"
                : l.AccountCode == laborAccount ? "labor" : l.AccountCode == equipmentAccount ? "equipment" : "other";
            if (isWip) r.WipBalance += amount;
            if (reference is Guid invoiceRef && rentalRatios.TryGetValue(invoiceRef, out var rentalRatio)
                && (source is "purchase" or "purchase_return"))
            {
                var rentalAmount = DocumentPricing.Round(amount * rentalRatio);
                if (rentalAmount != 0)
                    costLines.Add(("rented_equipment", l.JournalEntryId, l.EntryNumber, l.Date, l.SourceType, l.ReferenceNumber, l.Description, l.AccountCode, l.NameAr, rentalAmount));
                if (amount - rentalAmount != 0)
                    costLines.Add(("other", l.JournalEntryId, l.EntryNumber, l.Date, l.SourceType, l.ReferenceNumber, l.Description, l.AccountCode, l.NameAr, amount - rentalAmount));
                continue;
            }
            costLines.Add((category, l.JournalEntryId, l.EntryNumber, l.Date, l.SourceType, l.ReferenceNumber, l.Description, l.AccountCode, l.NameAr, amount));
        }
        // الاحتساب المزدوج: تكلفة محمّلة بالساعات من التقارير اليومية ومرحّلة أيضاً على مركز المشروع من الرواتب أو الإهلاك
        foreach (var d in await DuplicatesAsync(contractId, cc, ct))
        {
            costLines.Add((d.Category, null, null, d.Date, DuplicateSource, null, d.Description, null, null, -d.Amount));
            r.DuplicateExcluded += d.Amount;
        }
        if (r.DuplicateExcluded > 0)
            r.Notes.Add($"استُبعد {r.DuplicateExcluded:N2} محمّل مرتين: رواتب أو إهلاك على مركز المشروع لموظفين وأصول تُحمَّل بساعاتها من التقارير اليومية. انقل مركز تكلفتهم إلى مركز عام.");

        // الفعلي = كل أسطر التكلفة (ومنها غراماتنا على الباطن دائنة تخفض فئته)؛ والمعترف به = الفعلي − المواد تحت التنفيذ
        r.ActualCost = costLines.Sum(x => x.Amount);
        r.IndirectCost = costLines.Where(x => IndirectCategories.Contains(x.Category)).Sum(x => x.Amount);
        r.DirectCost = r.ActualCost - r.IndirectCost;
        r.ExpensedCost = r.ActualCost - r.WipBalance;
        r.BookedProfit = r.BookedRevenue - r.ClientDeductions - r.ExpensedCost;
        r.NetRevenue = r.BookedRevenue - r.ClientDeductions;
        r.NetMarginPercent = r.NetRevenue > 0 ? Math.Round(r.BookedProfit / r.NetRevenue * 100m, 2) : null;

        r.Accounts = costLines.Where(x => x.Account != null).GroupBy(x => (x.Account, x.AccountName, x.Category))
            .Select(g => new ProjectCostAccountDto { AccountCode = g.Key.Account!, AccountName = g.Key.AccountName ?? string.Empty, Category = g.Key.Category, Amount = g.Sum(x => x.Amount) })
            .Where(x => x.Amount != 0).OrderByDescending(x => Math.Abs(x.Amount)).ToList();
        r.Lines = costLines.GroupBy(x => (x.Entry, x.Number, x.Date, x.Source, x.RefNo, x.Description, x.Account, x.AccountName, x.Category))
            .Select(g => new ProjectCostLineDto
            {
                JournalEntryId = g.Key.Entry, EntryNumber = g.Key.Number, Date = g.Key.Date, SourceType = g.Key.Source, ReferenceNumber = g.Key.RefNo,
                Description = g.Key.Description, AccountCode = g.Key.Account, AccountName = g.Key.AccountName, Category = g.Key.Category,
                Group = IndirectCategories.Contains(g.Key.Category) ? "indirect" : "direct", Amount = g.Sum(x => x.Amount),
            })
            .Where(x => x.Amount != 0).OrderByDescending(x => x.Date).ThenBy(x => x.EntryNumber).ToList();
        var byCategory = costLines.GroupBy(x => x.Category).ToDictionary(g => g.Key, g => g.ToList());
        var docs = byCategory.ToDictionary(kv => kv.Key, kv => kv.Value
            .Where(x => x.Entry != null)
            .GroupBy(x => (Entry: x.Entry!.Value, Number: x.Number ?? string.Empty, x.Date, x.Source, x.RefNo, x.Description))
            .Select(g => new ProjectCostDocumentDto
            {
                JournalEntryId = g.Key.Entry, EntryNumber = g.Key.Number, Date = g.Key.Date, SourceType = g.Key.Source,
                ReferenceNumber = g.Key.RefNo, Description = g.Key.Description, Amount = g.Sum(x => x.Amount),
            }).Where(d => d.Amount != 0).OrderByDescending(d => Math.Abs(d.Amount)).Take(TopDocuments).ToList());
        Finish(r, planned, byCategory.ToDictionary(kv => kv.Key, kv => (kv.Value.Sum(x => x.Amount), docs[kv.Key])));
        return r;
    }

    /// <summary>ميزانية الخطة المعتمدة (آخر أساس مجمّد) موزعة على فئات التقرير.</summary>
    internal sealed record ApprovedPlan(int Revision, decimal Total, Dictionary<string, decimal> ByCategory, bool RevisionPending);

    /// <summary>
    /// آخر أساس خطة معتمد: الإجمالي (المواد + الموارد) والفئات — المواد، والعمالة، والمعدات المملوكة (وغير المحددة)، والمستأجرة،
    /// والباطن، والأخرى — من الموارد المجمّدة بملكيتها. الخطة في مراجعة لم تُعتمد يبقى حسابها على الأساس المعتمد.
    /// </summary>
    internal static async Task<ApprovedPlan?> ApprovedPlanBudgetAsync(ErpDbContext db, Guid contractId, CancellationToken ct)
    {
        var plan = await db.Set<ProjectExecutionPlan>().AsNoTracking().Where(p => p.ContractId == contractId)
            .Select(p => new { p.Status, p.BaselineSnapshotId }).FirstOrDefaultAsync(ct);
        if (plan?.BaselineSnapshotId is not Guid snapshotId) return null;
        var snap = await db.Set<ProjectSnapshot>().AsNoTracking().Where(s => s.Id == snapshotId)
            .Select(s => new { s.Revision, s.TotalCost, s.Json }).FirstOrDefaultAsync(ct);
        if (snap == null) return null;

        var by = new Dictionary<string, decimal> { ["materials"] = 0, ["labor"] = 0, ["equipment"] = 0, ["rented_equipment"] = 0, ["subcontract"] = 0, ["other"] = 0 };
        using (var doc = System.Text.Json.JsonDocument.Parse(snap.Json))
        {
            var root = doc.RootElement;
            static decimal Dec(System.Text.Json.JsonElement e, string name)
                => e.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number ? v.GetDecimal() : 0m;
            static string? Str(System.Text.Json.JsonElement e, string name)
                => e.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;
            if (root.TryGetProperty("budget", out var b)) by["materials"] = Dec(b, "materialsCost");
            if (root.TryGetProperty("resources", out var res) && res.ValueKind == System.Text.Json.JsonValueKind.Array)
                foreach (var x in res.EnumerateArray())
                {
                    var key = Str(x, "resourceType") switch
                    {
                        BoqResourceTypes.Labor => "labor",
                        BoqResourceTypes.Equipment => Str(x, "ownership") == EquipmentOwnerships.Rented ? "rented_equipment" : "equipment",
                        BoqResourceTypes.Subcontract => "subcontract",
                        _ => "other",
                    };
                    by[key] += Dec(x, "plannedCost");
                }
        }
        return new ApprovedPlan(snap.Revision, snap.TotalCost, by, plan.Status != ExecutionPlanStatuses.Approved);
    }

    /// <summary>الفئات مقابل المخطط، والقيمة المكتسبة والتوقع عند الاكتمال، ونسبة الإنجاز تكلفة إلى تكلفة.</summary>
    /// <param name="planned">المخطط لكل فئة: من الخطة المعتمدة لكل فئة، أو (بلا خطة) المواد والباطن و«execution» مجتمعاً للباقي.</param>
    private static void Finish(ProjectCostReportDto r, Dictionary<string, decimal?> planned,
        Dictionary<string, (decimal Actual, List<ProjectCostDocumentDto> Docs)> actual)
    {
        (decimal, List<ProjectCostDocumentDto>) Of(string key) => actual.TryGetValue(key, out var v) ? v : (0m, new());
        var bac = r.BudgetAtCompletion;
        foreach (var key in new[] { "materials", "labor", "equipment", "rented_equipment", "subcontract", "other", "overhead" })
        {
            var (act, docs) = Of(key);
            var p = planned.GetValueOrDefault(key);
            r.Categories.Add(new ProjectCostCategoryDto
            {
                Key = key, Group = IndirectCategories.Contains(key) ? "indirect" : "direct",
                Planned = p, Actual = act, Variance = p is decimal pv ? act - pv : null, TopDocuments = docs,
            });
        }
        if (planned.GetValueOrDefault("execution") is decimal pe)
        {
            var exec = r.Categories.Where(x => x.Key is "labor" or "equipment" or "rented_equipment" or "other").Sum(x => x.Actual);
            r.Categories.Add(new ProjectCostCategoryDto { Key = "execution", Planned = pe, Actual = exec, Variance = exec - pe });
        }

        r.EarnedValue = DocumentPricing.Round(bac * r.ProgressPercent / 100m);
        r.CostPerformanceIndex = r.ActualCost > 0 && r.EarnedValue > 0 ? Math.Round(r.EarnedValue / r.ActualCost, 3) : null;
        // بلا تكلفة فعلية بعد لا أساس للأداء: التوقع = الميزانية (لا يُفترض أن المشروع اكتمل بلا تكلفة)
        r.EstimateAtCompletion = r.ActualCost <= 0 ? bac
            : r.CostPerformanceIndex is decimal cpi && cpi > 0
            ? DocumentPricing.Round(bac / cpi)
            : DocumentPricing.Round(r.ActualCost + Math.Max(0, bac - r.EarnedValue));
        if (r.ActualCost <= 0 && r.ProgressPercent > 0)
            r.Notes.Add("أعمال معتمدة بلا تكلفة مسجلة على مركز المشروع: التوقع = الميزانية حتى تُسجَّل التكاليف (راجع ترحيل التقارير اليومية والصرف والفواتير).");
        // لا يُتوقع أقل مما صُرف فعلاً
        r.EstimateAtCompletion = Math.Max(r.EstimateAtCompletion, r.ActualCost);
        r.EstimateToComplete = r.EstimateAtCompletion - r.ActualCost;
        r.ForecastProfit = r.ContractValue - r.EstimateAtCompletion;
        r.ForecastMarginPercent = r.ContractValue > 0 ? Math.Round(r.ForecastProfit / r.ContractValue * 100m, 2) : null;
        r.ForecastOverrun = bac > 0 ? r.EstimateAtCompletion - bac : 0;
        if (r.ProgressPercent <= 0 && r.ActualCost > 0) r.Notes.Add("تكاليف بلا أعمال معتمدة بعد: التوقع = الفعلي + الميزانية المتبقية حتى يُعتمد أول مستخلص.");

        r.PocPercent = r.EstimateAtCompletion > 0 ? Math.Round(Math.Min(100m, r.ActualCost / r.EstimateAtCompletion * 100m), 2) : 0;
        r.PocRevenue = DocumentPricing.Round(r.ContractValue * r.PocPercent / 100m);
        r.PocProfit = r.PocRevenue - r.ActualCost;
    }

    private sealed record Duplicate(string Category, DateTime Date, string Description, decimal Amount);

    /// <summary>
    /// تكلفة محمّلة مرتين على مركز المشروع: موظف (عامل/مهندس) أو أصل (معدة مملوكة) مربوط بمورد في المشروع وحُمّلت ساعاته بقيود
    /// التقارير اليومية، ومركز تكلفته في مسير الرواتب أو الإهلاك هو مركز المشروع نفسه. يُستبعد الراتب المرحّل (الصافي قبل الاستقطاعات
    /// + حصة المنشأة في التأمينات، كما في قيد المسير) والإهلاك المرحّل؛ والمورد بلا ساعات محمّلة يبقى راتبه/إهلاكه تكلفته الوحيدة.
    /// <para>الاستبعاد لكل شهر (yyyy-MM) على حدة: راتب أو إهلاك شهر لا تحميل فيه يبقى كما هو، وما يُستبعد في الشهر لا يتجاوز ما حُمِّل
    /// فيه من التقارير (فلا تُمحى تكلفة حقيقية أكبر من المحمّل). المعدة بطريقة الإهلاك لا سعر ملكية في ساعتها، فإهلاكها تكلفتها الحقيقية
    /// ولا يُستبعد.</para>
    /// </summary>
    private async Task<List<Duplicate>> DuplicatesAsync(Guid contractId, Guid cc, CancellationToken ct)
    {
        var usage = await (from u in _db.Set<SiteResourceUsage>().AsNoTracking()
                           join rep in _db.Set<SiteDailyReport>().AsNoTracking() on u.ReportId equals rep.Id
                           join res in _db.Set<ProjectResource>().AsNoTracking() on u.ProjectResourceId equals res.Id
                           where rep.ContractId == contractId && rep.CostJournalEntryId != null && u.Cost > 0
                           select new { res.Kind, res.EmployeeId, res.FixedAssetId, res.EquipmentCostMethod, rep.ReportDate, u.Cost }).ToListAsync(ct);
        var labor = usage.Where(x => x.EmployeeId != null && x.Kind is ProjectResourceKinds.Labor or ProjectResourceKinds.Engineer)
            .Select(x => (x.EmployeeId!.Value, DuplicateCostMatcher.PeriodOf(x.ReportDate), x.Cost)).ToList();
        var owned = usage.Where(x => x.FixedAssetId != null && x.Kind == ProjectResourceKinds.OwnedEquipment
                && x.EquipmentCostMethod != EquipmentCostMethods.Depreciation)
            .Select(x => (x.FixedAssetId!.Value, DuplicateCostMatcher.PeriodOf(x.ReportDate), x.Cost)).ToList();
        var employees = labor.Select(x => x.Item1).Distinct().ToList();
        var assets = owned.Select(x => x.Item1).Distinct().ToList();

        var result = new List<Duplicate>();
        if (employees.Count > 0)
        {
            var payroll = await (from l in _db.Set<PayrollLine>().AsNoTracking()
                                 join run in _db.Set<PayrollRun>().AsNoTracking() on l.PayrollRunId equals run.Id
                                 where run.Status == "posted" && run.JournalEntryId != null && l.CostCenterId == cc && employees.Contains(l.EmployeeId)
                                 orderby run.Date, run.RunNumber
                                 select new { run.Date, run.Period, run.RunNumber, l.EmployeeId, l.EmployeeName, Amount = l.Gross - l.Deductions + l.EmployerGosi }).ToListAsync(ct);
            var excluded = DuplicateCostMatcher.Exclusions(labor, payroll.Select(x => (x.EmployeeId, x.Period, x.Amount)).ToList());
            for (var i = 0; i < payroll.Count; i++)
                if (excluded[i] > 0)
                    result.Add(new Duplicate("labor", payroll[i].Date,
                        $"استبعاد راتب {payroll[i].EmployeeName} ({payroll[i].RunNumber} · {payroll[i].Period}): محمّل بساعاته من التقارير اليومية", excluded[i]));
        }
        if (assets.Count > 0)
        {
            var depreciation = await (from d in _db.Set<FixedAssetDepreciation>().AsNoTracking()
                                      join a in _db.Set<FixedAsset>().AsNoTracking() on d.FixedAssetId equals a.Id
                                      join e in _db.Set<JournalEntry>().AsNoTracking() on d.JournalEntryId equals e.Id
                                      where d.CostCenterId == cc && assets.Contains(d.FixedAssetId) && e.Status != JournalEntryStatus.Draft
                                      orderby e.Date
                                      select new { e.Date, d.Period, d.FixedAssetId, a.NameAr, d.Amount }).ToListAsync(ct);
            var excluded = DuplicateCostMatcher.Exclusions(owned, depreciation.Select(x => (x.FixedAssetId, x.Period, x.Amount)).ToList());
            for (var i = 0; i < depreciation.Count; i++)
                if (excluded[i] > 0)
                    result.Add(new Duplicate("equipment", depreciation[i].Date,
                        $"استبعاد إهلاك {depreciation[i].NameAr} ({depreciation[i].Period}): محمّل بساعات تشغيله من التقارير اليومية", excluded[i]));
        }
        return result;
    }

    private async Task<decimal> SiteOperatingCostAsync(Guid contractId, CancellationToken ct)
    {
        var resources = await (from u in _db.Set<SiteResourceUsage>().AsNoTracking()
                               join r in _db.Set<SiteDailyReport>().AsNoTracking() on u.ReportId equals r.Id
                               where r.ContractId == contractId && r.Status == SiteDailyReportStatuses.Approved
                               select u.Cost).SumAsync(ct);
        var materials = await (from m in _db.Set<SiteMaterialUsage>().AsNoTracking()
                               join r in _db.Set<SiteDailyReport>().AsNoTracking() on m.ReportId equals r.Id
                               where r.ContractId == contractId && r.Status == SiteDailyReportStatuses.Approved
                               select m.Quantity * m.UnitCost).SumAsync(ct);
        return DocumentPricing.Round(resources + materials);
    }
}

/// <summary>
/// مطابقة التكلفة المحمّلة مرتين لكل (موظف أو أصل، شهر): ما يُستبعد من كل قيد راتب أو إهلاك = الأقل من مبلغه ومما بقي من المحمّل
/// بالتقارير اليومية في الشهر نفسه. القيود بترتيبها تتقاسم سقف شهرها، وشهر بلا تحميل لا يُستبعد منه شيء.
/// </summary>
public static class DuplicateCostMatcher
{
    /// <summary>الشهر بصيغة الرواتب والإهلاك (yyyy-MM).</summary>
    public static string PeriodOf(DateTime date) => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <returns>المستبعد لكل قيد بترتيب <paramref name="postings"/>.</returns>
    public static decimal[] Exclusions<TKey>(IEnumerable<(TKey Key, string Period, decimal Cost)> absorbed,
        IReadOnlyList<(TKey Key, string Period, decimal Amount)> postings) where TKey : notnull
    {
        var caps = absorbed.GroupBy(a => (a.Key, a.Period)).ToDictionary(g => g.Key, g => g.Sum(a => a.Cost));
        var result = new decimal[postings.Count];
        for (var i = 0; i < postings.Count; i++)
        {
            var (key, period, amount) = postings[i];
            if (amount <= 0 || !caps.TryGetValue((key, period), out var cap) || cap <= 0) continue;
            var taken = Math.Min(amount, cap);
            caps[(key, period)] = cap - taken;
            result[i] = DocumentPricing.Round(taken);
        }
        return result;
    }
}
