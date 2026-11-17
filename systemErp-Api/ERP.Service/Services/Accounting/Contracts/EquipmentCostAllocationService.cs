using System.Globalization;
using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// توزيع الإهلاك الفعلي لمعدات «طريقة الإهلاك» على المشاريع (<see cref="EquipmentCostMethods.Depreciation"/>).
/// <list type="bullet">
/// <item>المصدر: إهلاك الأصل المرحّل للفترة (<see cref="FixedAssetDepreciation"/>) كما هو، بلا إعادة حساب.</item>
/// <item>الحصة: الإهلاك × ساعات وجود المعدة في تقارير المشروع المعتمدة (تشغيل وإضافي وتوقف واستعداد) ÷ max(طاقة الشهر، مجموع
/// ساعاتها). الباقي (عطل بلا مشروع) يبقى على مركز الأصل. حين تبلغ الساعات الطاقة يُوزَّع الإهلاك كله وفرق التقريب على آخر مشروع.</item>
/// <item>الساعات من موارد أُسندت بطريقة الإهلاك وحدها (لقطة <see cref="ProjectResource.EquipmentCostMethod"/>): ساعات إسناد سابق بسعر
/// داخلي حُمِّلت بسعرها فلا تأخذ حصة إهلاك أيضاً.</item>
/// <item>القيد: مدين «تكلفة معدات المشاريع» بمركز المشروع / دائن «التكاليف المحمّلة على المشاريع» بلا مركز، كتحميل التقرير اليومي؛
/// فيبقى الإهلاك مرة واحدة في مصروف المنشأة. مشروع مركزه هو مركز الأصل لا يأخذ حصة (إهلاكه عليه أصلاً).</item>
/// <item>كل حساب يرحّل الفرق عمّا رُحِّل: الأول بتاريخ نهاية الفترة، وما بعده بتاريخ اليوم (الفترة قد تكون أُقفلت).</item>
/// </list>
/// </summary>
public class EquipmentCostAllocationService : IEquipmentCostAllocationService
{
    public const string SourceType = "equipment_depreciation_allocation";
    private const int HourDecimals = 4;
    private readonly ErpDbContext _db;
    private readonly IAccountingPostingService _posting;

    public EquipmentCostAllocationService(ErpDbContext db, IAccountingPostingService posting)
    {
        _db = db; _posting = posting;
    }

    private sealed record Target(Guid CostCenterId, decimal Hours, decimal Amount);

    public async Task ReallocateAsync(Guid fixedAssetId, string period, CancellationToken ct = default)
    {
        var (start, end) = ParsePeriod(period);
        var existing = await _db.Set<EquipmentDepreciationAllocation>().Where(a => a.FixedAssetId == fixedAssetId && a.Period == period).ToListAsync(ct);
        var equipment = await _db.Set<Equipment>().AsNoTracking().FirstOrDefaultAsync(e => e.FixedAssetId == fixedAssetId, ct);
        var depreciation = await _db.Set<FixedAssetDepreciation>().AsNoTracking()
            .Where(d => d.FixedAssetId == fixedAssetId && d.Period == period).Select(d => new { d.Amount, d.CostCenterId }).FirstOrDefaultAsync(ct);

        var targets = new Dictionary<Guid, Target>();
        decimal basis = 0;
        if (equipment != null && depreciation is { Amount: > 0 })
        {
            var resources = from a in _db.Set<EquipmentAssignment>()
                            join r in _db.Set<ProjectResource>() on a.ProjectResourceId equals r.Id
                            where a.EquipmentId == equipment.Id && r.EquipmentCostMethod == EquipmentCostMethods.Depreciation
                            select r.Id;
            var hours = (await (from u in _db.Set<SiteResourceUsage>().AsNoTracking()
                                join rep in _db.Set<SiteDailyReport>().AsNoTracking() on u.ReportId equals rep.Id
                                where rep.Status == SiteDailyReportStatuses.Approved && rep.ReportDate >= start && rep.ReportDate <= end
                                      && resources.Contains(u.ProjectResourceId)
                                group u by rep.ContractId into g
                                select new { ContractId = g.Key, Hours = g.Sum(x => x.Hours + x.OvertimeHours + x.IdleHours + x.StandbyHours) })
                    .ToListAsync(ct))
                .Where(h => h.Hours > 0).OrderBy(h => h.ContractId).ToList();
            var contractIds = hours.Select(h => h.ContractId).ToList();
            var centers = await _db.Set<CommercialContract>().AsNoTracking().Where(c => contractIds.Contains(c.Id))
                .Select(c => new { c.Id, c.CostCenterId }).ToDictionaryAsync(c => c.Id, c => c.CostCenterId, ct);
            var capacity = equipment.HoursPerDay * equipment.DaysPerMonth;
            var total = hours.Sum(h => h.Hours);
            basis = Math.Max(capacity, total);
            if (basis > 0)
            {
                // مشروع بلا مركز يُرحَّل لاحقاً (كتحميل التقرير)، ومشروع مركزه مركز الأصل يحمل إهلاكه أصلاً
                var eligible = hours.Where(h => centers.GetValueOrDefault(h.ContractId) is Guid cc && cc != depreciation.CostCenterId).ToList();
                var amounts = eligible.Select(h => DocumentPricing.Round(depreciation.Amount * h.Hours / basis)).ToList();
                if (total >= capacity && eligible.Count == hours.Count && amounts.Count > 0)
                    amounts[^1] = depreciation.Amount - amounts.Take(amounts.Count - 1).Sum();
                for (var i = 0; i < eligible.Count; i++)
                    targets[eligible[i].ContractId] = new Target(centers[eligible[i].ContractId]!.Value, Math.Round(eligible[i].Hours, HourDecimals), amounts[i]);
            }
        }

        // الفرق عمّا رُحِّل لكل مشروع؛ تغيّر مركز المشروع يعكس القديم على مركزه ويرحّل الجديد كاملاً على الجديد
        var lines = new List<(Guid Center, decimal Amount)>();
        foreach (var contractId in existing.Select(a => a.ContractId).Union(targets.Keys))
        {
            var row = existing.FirstOrDefault(a => a.ContractId == contractId);
            var target = targets.GetValueOrDefault(contractId);
            if (row != null && target != null && row.CostCenterId != target.CostCenterId)
            {
                lines.Add((row.CostCenterId, -row.Amount));
                lines.Add((target.CostCenterId, target.Amount));
            }
            else lines.Add((target?.CostCenterId ?? row!.CostCenterId, (target?.Amount ?? 0) - (row?.Amount ?? 0)));
        }
        lines.RemoveAll(l => l.Amount == 0);

        Guid? journalId = null;
        if (lines.Count > 0)
        {
            await ContractAccountsSetup.EnsureMissingAsync(_db, ct);
            var settings = await PostingAccounts.SettingsAsync(_db, ct);
            var equipmentAccount = settings.ContractAccount(nameof(AccountingSettings.ProjectEquipmentCostAccountCode));
            var absorbedAccount = settings.ContractAccount(nameof(AccountingSettings.ProjectAbsorbedCostAccountCode));
            var label = equipment?.Code ?? fixedAssetId.ToString();
            var note = existing.Count == 0
                ? $"توزيع إهلاك المعدة {label} على المشاريع عن {period}"
                : $"فرق توزيع إهلاك المعدة {label} على المشاريع عن {period}";
            var posting = new List<PostingLine>();
            foreach (var (center, amount) in lines)
                posting.Add(amount > 0 ? new(equipmentAccount, amount, 0, note, center) : new(equipmentAccount, 0, -amount, note, center));
            var net = lines.Sum(l => l.Amount);
            if (net != 0) posting.Add(net > 0 ? new(absorbedAccount, 0, net, note, null) : new(absorbedAccount, -net, 0, note, null));
            var posted = await _posting.PostAsync(new GenericPostingRequest
            {
                Date = existing.Count == 0 ? end : SaudiTime.Today, Description = note, SourceType = SourceType,
                SourceId = equipment?.Id ?? fixedAssetId, SourceNumber = period, Lines = posting,
            }, ct);
            journalId = posted.JournalEntryId;
        }

        // سطر لكل مشروع بما رُحِّل له الآن؛ المشروع الذي سقطت حصته يُحذف سطره (القيود تحفظ التاريخ)
        foreach (var row in existing.Where(a => !targets.ContainsKey(a.ContractId))) _db.Remove(row);
        foreach (var (contractId, target) in targets)
        {
            var row = existing.FirstOrDefault(a => a.ContractId == contractId);
            if (row == null)
            {
                row = new EquipmentDepreciationAllocation { EquipmentId = equipment!.Id, FixedAssetId = fixedAssetId, Period = period, ContractId = contractId };
                _db.Add(row);
            }
            if (journalId != null && (row.Amount != target.Amount || row.CostCenterId != target.CostCenterId)) row.JournalEntryId = journalId;
            row.CostCenterId = target.CostCenterId; row.Hours = target.Hours; row.BasisHours = Math.Round(basis, HourDecimals);
            row.DepreciationAmount = depreciation!.Amount; row.Amount = target.Amount;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task ReallocateForReportAsync(Guid reportId, CancellationToken ct = default)
    {
        var report = await _db.Set<SiteDailyReport>().AsNoTracking().Where(r => r.Id == reportId)
            .Select(r => new { r.ContractId, r.ReportDate }).FirstOrDefaultAsync(ct);
        if (report == null) return;
        var period = Period(report.ReportDate);
        // معدات طريقة الإهلاك في التقرير، ومعدات وُزِّع لها على المشروع في الفترة (تقرير عُدِّل فسقطت منه)
        var used = await (from u in _db.Set<SiteResourceUsage>().AsNoTracking()
                          join r in _db.Set<ProjectResource>().AsNoTracking() on u.ProjectResourceId equals r.Id
                          join a in _db.Set<EquipmentAssignment>().AsNoTracking() on r.Id equals a.ProjectResourceId
                          join e in _db.Set<Equipment>().AsNoTracking() on a.EquipmentId equals e.Id
                          where u.ReportId == reportId && r.EquipmentCostMethod == EquipmentCostMethods.Depreciation && e.FixedAssetId != null
                          select e.FixedAssetId!.Value).Distinct().ToListAsync(ct);
        var allocated = await _db.Set<EquipmentDepreciationAllocation>().AsNoTracking()
            .Where(a => a.Period == period && a.ContractId == report.ContractId).Select(a => a.FixedAssetId).Distinct().ToListAsync(ct);
        foreach (var assetId in used.Union(allocated)) await ReallocateAsync(assetId, period, ct);
    }

    public Task<int> ReallocatePeriodAsync(string period, CancellationToken ct = default)
    {
        ParsePeriod(period);
        // إجراء مستقل: معاملة واحدة لكل الأصول (أو ينضم لمعاملة المستدعي)
        return new TransactionRunner(_db).RunAsync(async token =>
        {
            var assets = await _db.Set<Equipment>().AsNoTracking().Where(e => e.FixedAssetId != null).Select(e => e.FixedAssetId!.Value).ToListAsync(token);
            var allocated = await _db.Set<EquipmentDepreciationAllocation>().AsNoTracking().Where(a => a.Period == period).Select(a => a.FixedAssetId).ToListAsync(token);
            var all = assets.Union(allocated).ToList();
            foreach (var assetId in all) await ReallocateAsync(assetId, period, token);
            return all.Count;
        }, ct);
    }

    public static string Period(DateTime date) => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static (DateTime Start, DateTime End) ParsePeriod(string period)
    {
        if (!DateTime.TryParseExact(period, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
            throw new ValidationFailedException("الفترة بصيغة yyyy-MM.");
        return (start, start.AddMonths(1).AddDays(-1));
    }
}
