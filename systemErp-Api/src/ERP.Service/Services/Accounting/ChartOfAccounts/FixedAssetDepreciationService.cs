using System.Globalization;
using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// إهلاك الأصول الثابتة بالفترة الشهرية: قسط ثابت = (تكلفة الشراء − القيمة التخريدية) × النسبة السنوية ÷ 12، بدءاً من شهر
/// الشراء، ولا تنزل القيمة الدفترية دون القيمة التخريدية. الترحيل قيد واحد للفترة عبر المحرك المحاسبي (مدين مصروف الإهلاك / دائن مجمع الإهلاك لكل أصل)
/// ومركز تكلفة الأصل على السطرين، مع سجل لكل أصل (بمركزه ومستودعه وقت الترحيل) يمنع تكرار الفترة — كل ذلك في معاملة واحدة.
/// فترات الأصل تُرحَّل متتالية: بعد أول فترة مرحَّلة لا تُقبل إلا الفترة التالية مباشرة (أول فترة حرّة لتبدأ الأصول القائمة من أي شهر).
/// قيد الإهلاك لا يُعكس ولا يُعدَّل من شاشة القيود: عكسه من هنا (ReverseRunAsync) يعيد أرصدة الأصول ويحذف سجلات الفترة معه.
/// </summary>
public class FixedAssetDepreciationService : IFixedAssetDepreciationService
{
    private const string SourceType = "fixed_asset_depreciation";
    private const string Ready = "ready", Posted = "posted", Blocked = "blocked";

    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;
    private readonly ITransactionRunner _tx;
    private readonly IAccountingPostingService _posting;
    private readonly IAuditService _audit;

    public FixedAssetDepreciationService(ErpDbContext db, ICurrentUser user, ITransactionRunner tx, IAccountingPostingService posting, IAuditService audit)
    {
        _db = db; _user = user; _tx = tx; _posting = posting; _audit = audit;
    }

    private sealed record Planned(DepreciationLineDto Line, string ExpenseAccountCode, string AccumulatedAccountCode);

    public async Task<DepreciationRunDto> PreviewAsync(DepreciationRunRequestDto request, CancellationToken ct = default)
    {
        var (period, _, plan) = await PlanAsync(request, ct);
        return ToRun(period, plan.Select(p => p.Line), isPreview: true);
    }

    public Task<DepreciationRunDto> PostAsync(DepreciationRunRequestDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var (period, periodEnd, plan) = await PlanAsync(request, token);
            var ready = plan.Where(p => p.Line.Status == Ready).ToList();
            var notReady = plan.Where(p => p.Line.Status != Ready).Select(p => p.Line).ToList();

            // أصول محددة بالاسم: أي أصل منها لا يُرحَّل يُفشل الطلب كله بسببه الصريح.
            if (request.AssetIds is { Count: > 0 } && notReady.Count > 0)
            {
                if (notReady[0].Status == Posted) throw new ConflictException(notReady[0].Reason!);
                throw new ValidationFailedException(notReady[0].Reason!, notReady.Select(l => l.Reason!));
            }
            if (ready.Count == 0)
                throw new ValidationFailedException(Messages.NoAssetsToDepreciate,
                    notReady.Count > 0 ? notReady.Select(l => l.Reason!) : null);

            var lines = new List<PostingLine>();
            foreach (var p in ready)
            {
                var note = $"إهلاك {p.Line.AssetCode} عن {period}";
                lines.Add(new(p.ExpenseAccountCode, p.Line.Amount, 0, note, p.Line.CostCenterId));
                lines.Add(new(p.AccumulatedAccountCode, 0, p.Line.Amount, note, p.Line.CostCenterId));
            }
            var journal = await _posting.PostAsync(new GenericPostingRequest
            {
                Date = periodEnd, Description = $"إهلاك الأصول الثابتة عن الفترة {period}",
                SourceType = SourceType, SourceNumber = period, Lines = lines,
            }, token);

            var now = DateTime.UtcNow;
            foreach (var p in ready)
            {
                var amount = p.Line.Amount;
                // تحديث نسبي مشروط في قاعدة البيانات: ترحيل متزامن لفترة أخرى لا يُضيِّع أثراً ولا يُنزل القيمة الدفترية تحت الصفر.
                var updated = await _db.Set<FixedAsset>().Where(a => a.Id == p.Line.FixedAssetId && a.CurrentBookValue >= amount)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(a => a.AccumulatedDepreciation, a => (a.AccumulatedDepreciation ?? a.PurchaseCost - a.CurrentBookValue) + amount)
                        .SetProperty(a => a.CurrentBookValue, a => a.CurrentBookValue - amount)
                        .SetProperty(a => a.UpdatedAt, now), token);
                if (updated != 1) throw new ConflictException(Messages.DepreciationPostedConcurrently);

                _db.Add(new FixedAssetDepreciation
                {
                    FixedAssetId = p.Line.FixedAssetId, Period = period, Amount = amount, CostCenterId = p.Line.CostCenterId!.Value, WarehouseId = p.Line.WarehouseId,
                    JournalEntryId = journal.JournalEntryId, JournalEntryNumber = journal.EntryNumber,
                    PostedAt = now, PostedByUserId = _user.UserId, PostedBy = _user.Name,
                });
            }
            try
            {
                await _db.SaveChangesAsync(token);
            }
            catch (DbUpdateException ex) when ((ex.InnerException?.Message ?? ex.Message).Contains("duplicate", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException(Messages.DepreciationPostedConcurrently); // الفهرس الفريد (الأصل + الفترة)
            }

            await _audit.LogAsync("DEPRECIATION_POSTED", nameof(FixedAsset), period,
                $"ترحيل إهلاك الفترة {period}: {ready.Count} أصل بمبلغ {ready.Sum(p => p.Line.Amount):0.00}، القيد {journal.EntryNumber}", token);

            foreach (var p in ready) { p.Line.Status = Posted; p.Line.Reason = null; }
            var result = ToRun(period, plan.Select(p => p.Line), isPreview: false);
            result.JournalEntryId = journal.JournalEntryId;
            result.JournalEntryNumber = journal.EntryNumber;
            result.ReadyCount = ready.Count;
            result.TotalAmount = ready.Sum(p => p.Line.Amount);
            return result;
        }, ct);

    /// <summary>
    /// يعكس ترحيل إهلاك كامل (قيده وكل سجلاته): يعيد لكل أصل مجمع إهلاكه وقيمته الدفترية ويحذف سجل الفترة.
    /// لا يُعكس إلا آخر فترة مرحَّلة لكل أصل، ولا لأصل استُبعد بعدها.
    /// </summary>
    public Task ReverseRunAsync(Guid journalEntryId, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var records = await _db.Set<FixedAssetDepreciation>().Where(d => d.JournalEntryId == journalEntryId).ToListAsync(token);
            if (records.Count == 0) throw new NotFoundException(Messages.DepreciationRunNotFound);
            var period = records[0].Period;
            var assetIds = records.Select(d => d.FixedAssetId).ToList();
            var assets = await _db.Set<FixedAsset>().Where(a => assetIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, token);
            var later = await _db.Set<FixedAssetDepreciation>().AsNoTracking()
                .Where(d => assetIds.Contains(d.FixedAssetId) && string.Compare(d.Period, period) > 0).Select(d => d.FixedAssetId).Distinct().ToListAsync(token);

            foreach (var record in records)
            {
                var asset = assets[record.FixedAssetId];
                if (later.Contains(asset.Id)) throw new ConflictException(string.Format(Messages.DepreciationReverseLatestOnly, asset.AssetCode));
                if (asset.DisposedAt.HasValue) throw new ConflictException(string.Format(Messages.AssetAlreadyDisposed, asset.AssetCode));
                asset.AccumulatedDepreciation = (asset.AccumulatedDepreciation ?? asset.PurchaseCost - asset.CurrentBookValue) - record.Amount;
                asset.CurrentBookValue += record.Amount;
            }
            _db.RemoveRange(records);
            await _db.SaveChangesAsync(token);
            await _posting.ReverseAsync(journalEntryId, $"عكس إهلاك الفترة {period}", token);
            await _audit.LogAsync("DEPRECIATION_REVERSED", nameof(FixedAsset), period,
                $"عكس إهلاك الفترة {period}: {records.Count} أصل بمبلغ {records.Sum(d => d.Amount):0.00}", token);
        }, ct);

    public async Task<PagedResult<FixedAssetDepreciationDto>> ListAsync(Guid? fixedAssetId, Guid? costCenterId, Guid? warehouseId, string? period, PaginationParams p, CancellationToken ct = default)
    {
        var q = _db.Set<FixedAssetDepreciation>().AsNoTracking();
        if (fixedAssetId.HasValue) q = q.Where(d => d.FixedAssetId == fixedAssetId);
        if (costCenterId.HasValue) q = q.Where(d => d.CostCenterId == costCenterId);
        if (warehouseId.HasValue) q = q.Where(d => d.WarehouseId == warehouseId);
        if (!string.IsNullOrWhiteSpace(period)) q = q.Where(d => d.Period == period.Trim());
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(d => d.Period).ThenByDescending(d => d.PostedAt)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);
        return new PagedResult<FixedAssetDepreciationDto>
        {
            Items = items.Select(Mapper.Map<FixedAssetDepreciationDto>).ToList(), TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    // ---------------- الحساب ----------------
    /// <summary>يحسب قسط الفترة لكل أصل ويحدّد حالته: جاهز، أو مرحَّل مسبقاً، أو ممنوع مع سببه. بلا أي أثر محفوظ.</summary>
    private async Task<(string Period, DateTime PeriodEnd, List<Planned> Plan)> PlanAsync(DepreciationRunRequestDto request, CancellationToken ct)
    {
        if (!DateTime.TryParseExact(request.Period?.Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
            throw new ValidationFailedException(Messages.DepreciationPeriodInvalid);
        if (start > DateTime.UtcNow) throw new ValidationFailedException(Messages.DepreciationPeriodInFuture);
        var period = start.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var periodEnd = start.AddMonths(1).AddDays(-1);

        var q = _db.Set<FixedAsset>().AsNoTracking();
        if (request.AssetIds is { Count: > 0 })
        {
            var ids = request.AssetIds.Distinct().ToList();
            q = q.Where(a => ids.Contains(a.Id));
            if (await q.CountAsync(ct) != ids.Count) throw new NotFoundException(string.Format(Messages.EntityNotFound, Messages.LabelFixedAsset));
        }
        if (request.CostCenterId.HasValue) q = q.Where(a => a.CostCenterId == request.CostCenterId);
        if (request.WarehouseId.HasValue) q = q.Where(a => a.WarehouseId == request.WarehouseId);
        var assets = await q.OrderBy(a => a.AssetCode).ToListAsync(ct);

        var assetIds = assets.Select(a => a.Id).ToList();
        var alreadyPosted = await _db.Set<FixedAssetDepreciation>().AsNoTracking()
            .Where(d => d.Period == period && assetIds.Contains(d.FixedAssetId)).ToDictionaryAsync(d => d.FixedAssetId, d => d.Amount, ct);
        // آخر فترة مرحَّلة لكل أصل (صيغة yyyy-MM تُرتَّب نصياً): الفترات تُرحَّل متتالية بلا فجوة ولا رجوع.
        var lastPosted = await _db.Set<FixedAssetDepreciation>().AsNoTracking().Where(d => assetIds.Contains(d.FixedAssetId))
            .GroupBy(d => d.FixedAssetId).Select(g => new { g.Key, Last = g.Max(d => d.Period) }).ToDictionaryAsync(x => x.Key, x => x.Last, ct);
        var centerIds = assets.Where(a => a.CostCenterId.HasValue).Select(a => a.CostCenterId!.Value).Distinct().ToList();
        var activeCenters = await _db.Set<CostCenter>().AsNoTracking().Where(c => centerIds.Contains(c.Id) && c.IsActive).Select(c => c.Id).ToListAsync(ct);
        var accountIds = assets.SelectMany(a => new[] { a.DepreciationExpenseAccountId ?? Guid.Empty, a.AccumulatedDepreciationAccountId }).Distinct().ToList();
        var accountCodes = await _db.Set<Account>().AsNoTracking().Where(a => accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Code, ct);

        var plan = new List<Planned>();
        foreach (var a in assets)
        {
            var line = new DepreciationLineDto
            {
                FixedAssetId = a.Id, AssetCode = a.AssetCode, NameAr = a.NameAr, NameEn = a.NameEn, CostCenterId = a.CostCenterId, WarehouseId = a.WarehouseId,
                BookValueBefore = a.CurrentBookValue, BookValueAfter = a.CurrentBookValue, Status = Blocked,
            };
            string expense = string.Empty, accumulated = string.Empty;

            if (alreadyPosted.TryGetValue(a.Id, out var postedAmount))
            {
                line.Status = Posted; line.Amount = postedAmount;
                line.Reason = string.Format(Messages.DepreciationAlreadyPosted, a.AssetCode, period);
            }
            else if (a.DisposedAt.HasValue) line.Reason = string.Format(Messages.AssetAlreadyDisposed, a.AssetCode);
            else if (a.PurchaseDate.Date > periodEnd) line.Reason = string.Format(Messages.AssetNotAcquiredInPeriod, a.AssetCode);
            else if (lastPosted.TryGetValue(a.Id, out var last) && NextPeriod(last!) != period)
                line.Reason = string.Format(Messages.DepreciationPeriodOutOfOrder, a.AssetCode, NextPeriod(last!));
            else if (a.CostCenterId == null) line.Reason = string.Format(Messages.DepreciationCostCenterRequired, a.AssetCode);
            else if (!activeCenters.Contains(a.CostCenterId.Value)) line.Reason = string.Format(Messages.DepreciationCostCenterInactive, a.AssetCode);
            else if (a.DepreciationExpenseAccountId == null || !accountCodes.TryGetValue(a.DepreciationExpenseAccountId.Value, out expense!))
                line.Reason = string.Format(Messages.DepreciationExpenseAccountRequired, a.AssetCode);
            else if (!accountCodes.TryGetValue(a.AccumulatedDepreciationAccountId, out accumulated!))
                line.Reason = Messages.AccumulatedDepreciationAccountNotFound;
            else
            {
                // لا إهلاك دون القيمة التخريدية: القسط على التكلفة ناقصها، والقسط الأخير هو المتبقي فوقها
                var monthly = Math.Round((a.PurchaseCost - a.SalvageValue) * a.DepreciationRate / 1200m, 2, MidpointRounding.AwayFromZero);
                var remaining = Math.Floor((a.CurrentBookValue - a.SalvageValue) * 100m) / 100m;
                var amount = Math.Min(monthly, remaining);
                if (amount <= 0) line.Reason = string.Format(Messages.AssetHasNoDepreciableBalance, a.AssetCode);
                else
                {
                    line.Status = Ready; line.Amount = amount; line.BookValueAfter = a.CurrentBookValue - amount;
                }
            }
            plan.Add(new Planned(line, expense ?? string.Empty, accumulated ?? string.Empty));
        }
        return (period, periodEnd, plan);
    }

    private static string NextPeriod(string period)
        => DateTime.ParseExact(period, "yyyy-MM", CultureInfo.InvariantCulture).AddMonths(1).ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static DepreciationRunDto ToRun(string period, IEnumerable<DepreciationLineDto> lines, bool isPreview)
    {
        var list = lines.ToList();
        var ready = list.Where(l => l.Status == Ready).ToList();
        return new DepreciationRunDto
        {
            Period = period, IsPreview = isPreview, Lines = list, ReadyCount = ready.Count, TotalAmount = ready.Sum(l => l.Amount),
        };
    }
}
