using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
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
/// </summary>
public class ProjectCostService : IProjectCostService
{
    /// <summary>مصدر قيد تحميل العمالة والمعدات من التقارير اليومية المعتمدة.</summary>
    public const string SiteCostSource = "site_report_cost";

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
        var r = new ProjectCostReportDto
        {
            ContractId = c.Id, CostCenterId = c.CostCenterId, ContractValue = c.ContractValue, EstimatedCost = c.EstimatedCost,
            ProjectBudget = budget, BudgetAtCompletion = c.EstimatedCost > 0 ? c.EstimatedCost : budget,
        };
        r.PlannedMargin = r.ContractValue - r.BudgetAtCompletion;
        if (c.EstimatedCost <= 0 && budget <= 0) r.Notes.Add("لا تكلفة تقديرية في جدول الكميات ولا ميزانية للمشروع: التوقع والانحراف بلا أساس.");

        // الإنجاز المعتمد للعميل
        r.CertifiedValue = await _db.Set<ContractCertificate>().AsNoTracking()
            .Where(x => x.ContractId == contractId && (x.Status == CertSt.Approved || x.Status == CertSt.Invoiced)).SumAsync(x => x.CurrentWorkValue, ct);
        r.ProgressPercent = c.ContractValue > 0 ? Math.Round(r.CertifiedValue / c.ContractValue * 100m, 2) : 0;
        r.SiteOperatingCost = await SiteOperatingCostAsync(contractId, ct);
        var unposted = await _db.Set<SiteDailyReport>().AsNoTracking()
            .CountAsync(x => x.ContractId == contractId && x.Status == SiteDailyReportStatuses.Approved && x.CostJournalEntryId == null && x.Resources.Any(u => u.Cost > 0), ct);
        r.UnpostedSiteReports = unposted;
        if (unposted > 0)
            r.Notes.Add($"{unposted} تقرير يومي معتمد لم تُحمَّل عمالته ومعداته على المشروع (اعتُمد قبل تفعيل التحميل): رحّلها من قسم التقارير اليومية.");

        // المخطط حسب الفئة: المواد من الاحتياجات المعتمدة، والباطن من عقوده، والباقي تنفيذ مباشر (عمالة ومعدات وأخرى) مجتمعاً
        var subs = await _db.Set<CommercialContract>().AsNoTracking()
            .Where(x => x.ParentContractId == contractId && x.Stage != ContractStageDefinition.Cancelled)
            .Select(x => new { x.Id, x.ContractValue }).ToListAsync(ct);
        decimal? plannedMaterials = null;
        try
        {
            var plan = await _materials.GetAsync(contractId, ct);
            if (plan.Status == ProjectMaterialPlanStatuses.Approved) plannedMaterials = plan.EstimatedCost;
        }
        catch (NotFoundException) { }
        if (plannedMaterials == null) r.Notes.Add("احتياجات المواد غير معتمدة: لا مخطط للمواد.");
        var plannedSub = subs.Sum(s => s.ContractValue);

        if (c.CostCenterId is not Guid cc)
        {
            r.MissingCostCenter = true;
            r.Notes.Add("العقد بلا مركز تكلفة: لا قيود تُنسب للمشروع.");
            Finish(r, plannedMaterials, plannedSub, new());
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

        var costLines = new List<(string Category, Guid Entry, string Number, DateTime Date, string? Source, string? RefNo, string Description, string Account, string AccountName, decimal Amount)>();
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
            var category = isSub ? "subcontract"
                : isWip || MaterialSources.Contains(l.SourceType ?? string.Empty) ? "materials"
                : l.SourceType == SiteCostSource ? (l.AccountCode == equipmentAccount ? "equipment" : l.AccountCode == laborAccount ? "labor" : "other")
                : LaborSources.Contains(l.SourceType ?? string.Empty) ? "labor"
                : EquipmentSources.Contains(l.SourceType ?? string.Empty) ? "equipment"
                : l.AccountCode == laborAccount ? "labor" : l.AccountCode == equipmentAccount ? "equipment" : "other";
            if (isWip) r.WipBalance += amount;
            costLines.Add((category, l.JournalEntryId, l.EntryNumber, l.Date, l.SourceType, l.ReferenceNumber, l.Description, l.AccountCode, l.NameAr, amount));
        }
        // الفعلي = كل أسطر التكلفة (ومنها غراماتنا على الباطن دائنة تخفض فئته)؛ والمعترف به = الفعلي − المواد تحت التنفيذ
        r.ActualCost = costLines.Sum(x => x.Amount);
        r.ExpensedCost = r.ActualCost - r.WipBalance;
        r.BookedProfit = r.BookedRevenue - r.ClientDeductions - r.ExpensedCost;

        r.Accounts = costLines.GroupBy(x => (x.Account, x.AccountName, x.Category))
            .Select(g => new ProjectCostAccountDto { AccountCode = g.Key.Account, AccountName = g.Key.AccountName, Category = g.Key.Category, Amount = g.Sum(x => x.Amount) })
            .Where(x => x.Amount != 0).OrderByDescending(x => Math.Abs(x.Amount)).ToList();
        var byCategory = costLines.GroupBy(x => x.Category).ToDictionary(g => g.Key, g => g.ToList());
        var docs = byCategory.ToDictionary(kv => kv.Key, kv => kv.Value
            .GroupBy(x => (x.Entry, x.Number, x.Date, x.Source, x.RefNo, x.Description))
            .Select(g => new ProjectCostDocumentDto
            {
                JournalEntryId = g.Key.Entry, EntryNumber = g.Key.Number, Date = g.Key.Date, SourceType = g.Key.Source,
                ReferenceNumber = g.Key.RefNo, Description = g.Key.Description, Amount = g.Sum(x => x.Amount),
            }).Where(d => d.Amount != 0).OrderByDescending(d => Math.Abs(d.Amount)).Take(TopDocuments).ToList());
        Finish(r, plannedMaterials, plannedSub, byCategory.ToDictionary(kv => kv.Key, kv => (kv.Value.Sum(x => x.Amount), docs[kv.Key])));
        return r;
    }

    /// <summary>الفئات مقابل المخطط، والقيمة المكتسبة والتوقع عند الاكتمال، ونسبة الإنجاز تكلفة إلى تكلفة.</summary>
    private static void Finish(ProjectCostReportDto r, decimal? plannedMaterials, decimal plannedSub,
        Dictionary<string, (decimal Actual, List<ProjectCostDocumentDto> Docs)> actual)
    {
        (decimal, List<ProjectCostDocumentDto>) Of(string key) => actual.TryGetValue(key, out var v) ? v : (0m, new());
        var bac = r.BudgetAtCompletion;
        // التنفيذ المباشر (العمالة والمعدات والأخرى) مخطط مجتمع: الميزانية − المواد − الباطن
        decimal? plannedExecution = bac > 0 ? Math.Max(0, bac - (plannedMaterials ?? 0) - plannedSub) : null;
        foreach (var key in new[] { "materials", "subcontract", "labor", "equipment", "other" })
        {
            var (act, docs) = Of(key);
            decimal? planned = key switch { "materials" => plannedMaterials, "subcontract" => plannedSub > 0 ? plannedSub : null, _ => null };
            r.Categories.Add(new ProjectCostCategoryDto { Key = key, Planned = planned, Actual = act, Variance = planned is decimal p ? act - p : null, TopDocuments = docs });
        }
        if (plannedExecution is decimal pe)
        {
            var exec = r.Categories.Where(x => x.Key is "labor" or "equipment" or "other").Sum(x => x.Actual);
            r.Categories.Add(new ProjectCostCategoryDto { Key = "execution", Planned = pe, Actual = exec, Variance = exec - pe });
        }

        r.EarnedValue = DocumentPricing.Round(bac * r.ProgressPercent / 100m);
        r.CostPerformanceIndex = r.ActualCost > 0 && r.EarnedValue > 0 ? Math.Round(r.EarnedValue / r.ActualCost, 3) : null;
        r.EstimateAtCompletion = r.CostPerformanceIndex is decimal cpi && cpi > 0
            ? DocumentPricing.Round(bac / cpi)
            : DocumentPricing.Round(r.ActualCost + Math.Max(0, bac - r.EarnedValue));
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
