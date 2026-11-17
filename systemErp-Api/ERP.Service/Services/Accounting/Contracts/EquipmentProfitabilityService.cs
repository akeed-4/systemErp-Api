using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using B = ERP.Core.Models.Accounting.EquipmentRevenueBases;
using K = ERP.Core.Models.Accounting.EquipmentLogKinds;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// إيرادات المعدات وربحيتها فوق ما هو قائم، بلا قيود ولا تكرار:
/// <list type="bullet">
/// <item>الإيراد التشغيلي: استخدام المعدة الفعلي من التقارير اليومية المعتمدة (وسجل التشغيل لغير المسندة) × تسعيرتها السارية يوم العمل،
/// بأساسها (ساعة/يوم/وردية/رحلة/مهمة). التسعيرة الخاصة بالعقد تتقدم على الافتراضية.</item>
/// <item>الإيراد المحاسبي: المبالغ الحالية لبنود جدول الكميات المربوطة بتسعيرات المعدة في المستخلصات المعتمدة والمفوترة (تاريخ المستخلص).</item>
/// <item>التكلفة الفعلية: الوقود والمصاريف والصيانة (بمشروعها وقت التسجيل)، واستحقاق الإيجار، والإهلاك المرحّل (حصص المشاريع لطريقة
/// الإهلاك، والباقي غير منسوب)، والمشغّل = ساعات العمل × سعره المعياري في بطاقة المعدة.</item>
/// </list>
/// </summary>
public class EquipmentProfitabilityService : IEquipmentProfitabilityService
{
    private readonly ErpDbContext _db;
    private readonly ITransactionRunner _tx;
    private readonly IAuditService _audit;

    public EquipmentProfitabilityService(ErpDbContext db, ITransactionRunner tx, IAuditService audit)
    {
        _db = db; _tx = tx; _audit = audit;
    }

    // =====================================================================
    // التسعيرات
    // =====================================================================

    public async Task<List<EquipmentRevenueRateDto>> RatesAsync(Guid? equipmentId, Guid? contractId, CancellationToken ct = default)
    {
        var q = _db.Set<EquipmentRevenueRate>().AsNoTracking().AsQueryable();
        if (equipmentId is Guid eq) q = q.Where(r => r.EquipmentId == eq);
        // عقد محدد: تسعيراته الخاصة والافتراضية لمعداته (المطبّقة عليه ما لم تُستبدل)
        if (contractId is Guid c) q = q.Where(r => r.ContractId == c || r.ContractId == null);
        return await RateDtosAsync(await q.OrderByDescending(r => r.EffectiveFrom).ToListAsync(ct), ct);
    }

    public Task<EquipmentRevenueRateDto> CreateRateAsync(SaveEquipmentRevenueRateDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var rate = new EquipmentRevenueRate();
            await ApplyRateAsync(rate, request, token);
            _db.Add(rate);
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("EQUIPMENT_REVENUE_RATE_CREATED", nameof(EquipmentRevenueRate), rate.Id.ToString(),
                $"تسعيرة إيراد • {rate.Basis} • {rate.Rate:0.00} • من {rate.EffectiveFrom:yyyy-MM-dd}", token);
            return (await RateDtosAsync(new List<EquipmentRevenueRate> { rate }, token)).Single();
        }, ct);

    public Task<EquipmentRevenueRateDto> UpdateRateAsync(Guid id, SaveEquipmentRevenueRateDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var rate = await FindRateAsync(id, token);
            await ApplyRateAsync(rate, request, token);
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("EQUIPMENT_REVENUE_RATE_UPDATED", nameof(EquipmentRevenueRate), rate.Id.ToString(),
                $"تسعيرة إيراد • {rate.Basis} • {rate.Rate:0.00} • من {rate.EffectiveFrom:yyyy-MM-dd}", token);
            return (await RateDtosAsync(new List<EquipmentRevenueRate> { rate }, token)).Single();
        }, ct);

    public Task DeleteRateAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var rate = await FindRateAsync(id, token);
            _db.Remove(rate);
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("EQUIPMENT_REVENUE_RATE_DELETED", nameof(EquipmentRevenueRate), id.ToString(),
                $"حذف تسعيرة إيراد • {rate.Basis} • {rate.Rate:0.00}", token);
        }, ct);

    /// <summary>بنود جدول كميات العقد القابلة للربط (غير المجموعات)، ومعدة كل بند مربوط.</summary>
    public async Task<List<EquipmentRevenueItemOptionDto>> ItemOptionsAsync(Guid contractId, CancellationToken ct = default)
    {
        var items = await _db.Set<CommercialContractItem>().AsNoTracking().Where(i => i.ContractId == contractId && !i.IsGroup)
            .OrderBy(i => i.SortOrder).ToListAsync(ct);
        var links = await (from r in _db.Set<EquipmentRevenueRate>().AsNoTracking()
                           join e in _db.Set<Equipment>().AsNoTracking() on r.EquipmentId equals e.Id
                           where r.ContractId == contractId && r.ContractItemId != null
                           select new { ItemId = r.ContractItemId!.Value, e.Id, e.Code }).ToListAsync(ct);
        var linkOf = links.GroupBy(l => l.ItemId).ToDictionary(g => g.Key, g => g.First());
        return items.Select(i => new EquipmentRevenueItemOptionDto
        {
            Id = i.Id, LineCode = i.LineCode, Description = i.Description, Unit = i.Unit, UnitPrice = i.UnitPrice,
            LinkedEquipmentId = linkOf.GetValueOrDefault(i.Id)?.Id, LinkedEquipmentCode = linkOf.GetValueOrDefault(i.Id)?.Code,
        }).ToList();
    }

    private async Task ApplyRateAsync(EquipmentRevenueRate rate, SaveEquipmentRevenueRateDto d, CancellationToken ct)
    {
        var errors = new List<string>();
        var equipment = await EquipmentService.FindAsync(_db, d.EquipmentId, track: false, ct);
        var basis = d.Basis?.Trim().ToLowerInvariant();
        if (!B.IsValid(basis)) errors.Add("اختر أساس احتساب الإيراد (ساعة، يوم، وردية، رحلة، مهمة).");
        if (d.Rate <= 0) errors.Add("أدخل سعر الإيراد لوحدة الأساس.");
        var shiftHours = d.ShiftHours ?? 8;
        if (basis == B.Shift && shiftHours is <= 0 or > 24) errors.Add("ساعات الوردية بين 1 و24.");
        var minimum = d.MinimumDailyHours ?? 0;
        if (minimum is < 0 or > 24) errors.Add("الحد الأدنى اليومي بين 0 و24 ساعة.");
        var idle = d.IdleBillingPercent ?? 0;
        var standby = d.StandbyBillingPercent ?? 0;
        if (idle is < 0 or > 100 || standby is < 0 or > 100) errors.Add("نسب فوترة التوقف والاستعداد بين 0 و100.");
        var from = d.EffectiveFrom?.Date ?? SaudiTime.Today;
        var to = d.EffectiveTo?.Date;
        if (to < from) errors.Add("نهاية السريان قبل بدايته.");
        if (d.ContractItemId != null && d.ContractId == null) errors.Add("ربط بند جدول الكميات يتطلب اختيار العقد.");
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        if (d.ContractId is Guid contractId && !await _db.Set<CommercialContract>().AnyAsync(c => c.Id == contractId, ct))
            throw new NotFoundException("العقد غير موجود.");
        if (d.ContractItemId is Guid itemId)
        {
            var item = await _db.Set<CommercialContractItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct)
                ?? throw new NotFoundException("بند جدول الكميات غير موجود.");
            if (item.ContractId != d.ContractId || item.IsGroup) throw new ValidationFailedException("البند ليس من بنود العقد المختار أو أنه مجموعة.");
            // البند يُفوتر عمل معدة واحدة، فلا يُنسب إيراد المستخلص الواحد لمعدتين
            var other = await (from r in _db.Set<EquipmentRevenueRate>().AsNoTracking()
                               join e in _db.Set<Equipment>().AsNoTracking() on r.EquipmentId equals e.Id
                               where r.ContractItemId == itemId && r.EquipmentId != d.EquipmentId
                               select e.Code).FirstOrDefaultAsync(ct);
            if (other != null) throw new ConflictException($"البند مربوط بالمعدة {other}؛ البند الواحد يُنسب لمعدة واحدة.");
        }
        // لا تتداخل فترتا سريان لتسعيرتين على المعدة نفسها والنطاق نفسه (افتراضي أو العقد نفسه)
        var overlap = await _db.Set<EquipmentRevenueRate>().AsNoTracking().AnyAsync(r => r.Id != rate.Id && r.EquipmentId == d.EquipmentId
            && r.ContractId == d.ContractId && r.EffectiveFrom <= (to ?? DateTime.MaxValue) && (r.EffectiveTo == null || r.EffectiveTo >= from), ct);
        if (overlap) throw new ConflictException($"للمعدة {equipment.Code} تسعيرة سارية في الفترة نفسها لهذا النطاق؛ أنهِ سريانها أولاً.");

        rate.EquipmentId = d.EquipmentId; rate.ContractId = d.ContractId; rate.ContractItemId = d.ContractItemId; rate.Basis = basis!;
        rate.Rate = d.Rate; rate.ShiftHours = basis == B.Shift ? shiftHours : 8;
        // الحد الأدنى ونسب التوقف للأسس الزمنية وحدها؛ الرحلة والمهمة بوحداتها فقط
        var timed = !B.IsUnitBased(basis!);
        rate.MinimumDailyHours = basis is B.Hour or B.Shift ? minimum : 0;
        rate.IdleBillingPercent = timed ? idle : 0; rate.StandbyBillingPercent = timed ? standby : 0;
        rate.EffectiveFrom = from; rate.EffectiveTo = to; rate.Notes = string.IsNullOrWhiteSpace(d.Notes) ? null : d.Notes.Trim()[..Math.Min(500, d.Notes.Trim().Length)];
    }

    private async Task<EquipmentRevenueRate> FindRateAsync(Guid id, CancellationToken ct)
        => await _db.Set<EquipmentRevenueRate>().FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("تسعيرة الإيراد غير موجودة.");

    private async Task<List<EquipmentRevenueRateDto>> RateDtosAsync(List<EquipmentRevenueRate> list, CancellationToken ct)
    {
        var equipmentIds = list.Select(r => r.EquipmentId).Distinct().ToList();
        var equipment = await _db.Set<Equipment>().AsNoTracking().Where(e => equipmentIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        var contractIds = list.Where(r => r.ContractId != null).Select(r => r.ContractId!.Value).Distinct().ToList();
        var contracts = await _db.Set<CommercialContract>().AsNoTracking().Where(c => contractIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => new { c.ContractNumber, Name = c.ProjectName ?? c.Title }, ct);
        var itemIds = list.Where(r => r.ContractItemId != null).Select(r => r.ContractItemId!.Value).Distinct().ToList();
        var items = await _db.Set<CommercialContractItem>().AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        return list.Select(r =>
        {
            var e = equipment.GetValueOrDefault(r.EquipmentId);
            var c = r.ContractId is Guid cid ? contracts.GetValueOrDefault(cid) : null;
            var i = r.ContractItemId is Guid iid ? items.GetValueOrDefault(iid) : null;
            return new EquipmentRevenueRateDto
            {
                Id = r.Id, EquipmentId = r.EquipmentId, EquipmentCode = e?.Code ?? string.Empty, EquipmentName = e?.NameAr ?? string.Empty, Category = e?.Category,
                ContractId = r.ContractId, ContractNumber = c?.ContractNumber, ProjectName = c?.Name, ContractItemId = r.ContractItemId,
                ContractItemLabel = i == null ? null : string.IsNullOrWhiteSpace(i.LineCode) ? i.Description : $"{i.LineCode} - {i.Description}",
                ContractItemUnit = i?.Unit, ContractItemUnitPrice = i?.UnitPrice, Basis = r.Basis, Rate = r.Rate, ShiftHours = r.ShiftHours,
                MinimumDailyHours = r.MinimumDailyHours, IdleBillingPercent = r.IdleBillingPercent, StandbyBillingPercent = r.StandbyBillingPercent,
                EffectiveFrom = r.EffectiveFrom, EffectiveTo = r.EffectiveTo, Notes = r.Notes, CreatedAt = r.CreatedAt,
            };
        }).ToList();
    }

    // =====================================================================
    // تقرير الربحية
    // =====================================================================

    /// <summary>يوم عمل لمعدة في مشروع (أو بلا مشروع من سجل التشغيل): مجمّع من أسطر التقرير اليومي على بنود اليوم.</summary>
    private sealed record WorkDay(Guid EquipmentId, Guid? ContractId, DateTime Date, decimal Hours, decimal Idle, decimal Standby, decimal Units);

    /// <summary>تراكم صف (معدة، مشروع) قبل التحويل لـ DTO.</summary>
    private sealed class Acc
    {
        public HashSet<string> Bases { get; } = new();
        public decimal WorkHours, IdleHours, WorkDays, WorkUnits, UnpricedHours, OperationalRevenue, CertifiedQuantity, AccountingRevenue;
        public decimal FuelCost, MaintenanceCost, Expenses, OperatorCost, Depreciation, RentalCost;
        public decimal TotalCost => FuelCost + MaintenanceCost + Expenses + OperatorCost + Depreciation + RentalCost;

        public void Add(Acc o)
        {
            Bases.UnionWith(o.Bases);
            WorkHours += o.WorkHours; IdleHours += o.IdleHours; WorkDays += o.WorkDays; WorkUnits += o.WorkUnits; UnpricedHours += o.UnpricedHours;
            OperationalRevenue += o.OperationalRevenue; CertifiedQuantity += o.CertifiedQuantity; AccountingRevenue += o.AccountingRevenue;
            FuelCost += o.FuelCost; MaintenanceCost += o.MaintenanceCost; Expenses += o.Expenses; OperatorCost += o.OperatorCost;
            Depreciation += o.Depreciation; RentalCost += o.RentalCost;
        }
    }

    public async Task<EquipmentProfitabilityReportDto> ReportAsync(DateTime? from, DateTime? to, Guid? contractId, CancellationToken ct = default)
    {
        var today = SaudiTime.Today;
        var end = to?.Date ?? today;
        var start = from?.Date ?? new DateTime(end.Year, end.Month, 1);
        if (end < start) throw new ValidationFailedException("نهاية الفترة قبل بدايتها.");

        var equipment = await _db.Set<Equipment>().AsNoTracking().OrderBy(e => e.Code).ToListAsync(ct);
        var byId = equipment.ToDictionary(e => e.Id);
        var links = await _db.Set<EquipmentAssignment>().AsNoTracking().Where(a => a.ProjectResourceId != null)
            .Select(a => new { a.EquipmentId, ResourceId = a.ProjectResourceId!.Value }).ToListAsync(ct);
        var equipmentOfResource = links.GroupBy(l => l.ResourceId).ToDictionary(g => g.Key, g => g.First().EquipmentId);
        var resourceIds = equipmentOfResource.Keys.ToList();

        // الاستخدام الفعلي: التقارير اليومية المعتمدة مجمّعة باليوم (أسطر المعدة على بنود متعددة يوم واحد)، ثم تشغيل غير المسندة من السجل
        var usage = await (from u in _db.Set<SiteResourceUsage>().AsNoTracking()
                           join r in _db.Set<SiteDailyReport>().AsNoTracking() on u.ReportId equals r.Id
                           where r.Status == SiteDailyReportStatuses.Approved && resourceIds.Contains(u.ProjectResourceId)
                                 && r.ReportDate >= start && r.ReportDate <= end
                           group u by new { u.ProjectResourceId, r.ContractId, r.ReportDate } into g
                           select new
                           {
                               g.Key.ProjectResourceId, g.Key.ContractId, g.Key.ReportDate,
                               Hours = g.Sum(x => x.Hours + x.OvertimeHours), Idle = g.Sum(x => x.IdleHours), Standby = g.Sum(x => x.StandbyHours),
                               Units = g.Sum(x => x.WorkUnits),
                           }).ToListAsync(ct);
        var days = usage.Select(u => new WorkDay(equipmentOfResource[u.ProjectResourceId], u.ContractId, u.ReportDate, u.Hours, u.Idle, u.Standby, u.Units)).ToList();
        var logs = await _db.Set<EquipmentLog>().AsNoTracking().Where(l => l.Date >= start && l.Date <= end)
            .Select(l => new { l.EquipmentId, l.Kind, l.ContractId, l.Date, l.Hours, l.Quantity, l.Amount }).ToListAsync(ct);
        days.AddRange(logs.Where(l => l.Kind == K.Operation).GroupBy(l => new { l.EquipmentId, l.ContractId, l.Date })
            .Select(g => new WorkDay(g.Key.EquipmentId, g.Key.ContractId, g.Key.Date, g.Sum(x => x.Hours), 0, 0, g.Sum(x => x.Quantity))));

        // التسعيرات السارية في الفترة
        var rates = await _db.Set<EquipmentRevenueRate>().AsNoTracking()
            .Where(r => r.EffectiveFrom <= end && (r.EffectiveTo == null || r.EffectiveTo >= start)).ToListAsync(ct);
        var ratesOf = rates.GroupBy(r => r.EquipmentId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.EffectiveFrom).ToList());
        EquipmentRevenueRate? RateFor(Guid equipmentId, Guid? contract, DateTime date)
        {
            if (!ratesOf.TryGetValue(equipmentId, out var list)) return null;
            bool Active(EquipmentRevenueRate r) => r.EffectiveFrom <= date && (r.EffectiveTo == null || r.EffectiveTo >= date);
            return (contract == null ? null : list.FirstOrDefault(r => r.ContractId == contract && Active(r))) ?? list.FirstOrDefault(r => r.ContractId == null && Active(r));
        }

        var acc = new Dictionary<(Guid EquipmentId, Guid? ContractId), Acc>();
        Acc At(Guid equipmentId, Guid? contract)
        {
            if (!acc.TryGetValue((equipmentId, contract), out var a)) acc[(equipmentId, contract)] = a = new Acc();
            return a;
        }

        foreach (var d in days.Where(d => byId.ContainsKey(d.EquipmentId)))
        {
            var e = byId[d.EquipmentId];
            var a = At(d.EquipmentId, d.ContractId);
            a.WorkHours += d.Hours; a.IdleHours += d.Idle + d.Standby; a.WorkUnits += d.Units;
            if (d.Hours > 0) a.WorkDays += 1;
            // المشغّل بسعره المعياري في بطاقة المعدة لساعات العمل
            a.OperatorCost += DocumentPricing.Round(d.Hours * e.OperatorPerHour);
            var rate = RateFor(d.EquipmentId, d.ContractId, d.Date);
            if (rate == null) { a.UnpricedHours += d.Hours; continue; }
            a.Bases.Add(rate.Basis);
            a.OperationalRevenue += DocumentPricing.Round(DayRevenue(rate, d));
        }

        // الإيراد المحاسبي: البنود المربوطة في المستخلصات المعتمدة والمفوترة (البند لمعدة واحدة)
        // الربط بأي تسعيرة للبند (ولو انتهى سريانها): المستخلص قد يُعتمد بعد انتهاء سعر العمل
        var equipmentOfItem = (await _db.Set<EquipmentRevenueRate>().AsNoTracking().Where(r => r.ContractItemId != null)
                .Select(r => new { ItemId = r.ContractItemId!.Value, r.EquipmentId }).ToListAsync(ct))
            .GroupBy(r => r.ItemId).ToDictionary(g => g.Key, g => g.First().EquipmentId);
        var linkedItems = equipmentOfItem.Keys.ToList();
        var certified = await (from l in _db.Set<ContractCertificateLine>().AsNoTracking()
                               join c in _db.Set<ContractCertificate>().AsNoTracking() on l.CertificateId equals c.Id
                               where (c.Status == ContractCertificateStatuses.Approved || c.Status == ContractCertificateStatuses.Invoiced)
                                     && c.CertificateDate >= start && c.CertificateDate <= end && linkedItems.Contains(l.ContractItemId)
                               select new { c.ContractId, l.ContractItemId, l.CurrentQuantity, l.CurrentAmount }).ToListAsync(ct);
        foreach (var l in certified)
        {
            var equipmentId = equipmentOfItem[l.ContractItemId];
            if (!byId.ContainsKey(equipmentId)) continue;
            var a = At(equipmentId, l.ContractId);
            a.CertifiedQuantity += l.CurrentQuantity; a.AccountingRevenue += l.CurrentAmount;
        }

        // التكاليف الفعلية بمشروعها وقت التسجيل
        foreach (var l in logs.Where(l => l.Kind is K.Fuel or K.Expense && byId.ContainsKey(l.EquipmentId)))
        {
            var a = At(l.EquipmentId, l.ContractId);
            if (l.Kind == K.Fuel) a.FuelCost += l.Amount; else a.Expenses += l.Amount;
        }
        var endStamp = end.AddDays(1);
        var orders = await _db.Set<EquipmentMaintenanceOrder>().AsNoTracking()
            .Where(o => o.Status == EquipmentMaintenanceStatuses.Completed && o.CompletedAt >= start && o.CompletedAt < endStamp)
            .Select(o => new { o.EquipmentId, o.ContractId, o.TotalCost }).ToListAsync(ct);
        foreach (var o in orders.Where(o => byId.ContainsKey(o.EquipmentId))) At(o.EquipmentId, o.ContractId).MaintenanceCost += o.TotalCost;
        var rental = await _db.Set<SiteRentalAccrual>().AsNoTracking()
            .Where(a => resourceIds.Contains(a.ProjectResourceId) && a.ReportDate >= start && a.ReportDate <= end)
            .GroupBy(a => new { a.ProjectResourceId, a.ContractId }).Select(g => new { g.Key.ProjectResourceId, g.Key.ContractId, Amount = g.Sum(x => x.Amount) })
            .ToListAsync(ct);
        foreach (var r in rental) At(equipmentOfResource[r.ProjectResourceId], r.ContractId).RentalCost += r.Amount;

        // الإهلاك المرحّل: حصص المشاريع (طريقة الإهلاك) على مشاريعها، والباقي على المعدة بلا مشروع
        string fromPeriod = start.ToString("yyyy-MM"), toPeriod = end.ToString("yyyy-MM");
        var assetIds = equipment.Where(e => e.FixedAssetId != null).Select(e => e.FixedAssetId!.Value).ToList();
        var depreciation = (await _db.Set<FixedAssetDepreciation>().AsNoTracking()
                .Where(d => assetIds.Contains(d.FixedAssetId) && string.Compare(d.Period, fromPeriod) >= 0 && string.Compare(d.Period, toPeriod) <= 0)
                .GroupBy(d => d.FixedAssetId).Select(g => new { g.Key, Amount = g.Sum(x => x.Amount) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Amount);
        var allocations = await _db.Set<EquipmentDepreciationAllocation>().AsNoTracking()
            .Where(a => string.Compare(a.Period, fromPeriod) >= 0 && string.Compare(a.Period, toPeriod) <= 0)
            .GroupBy(a => new { a.EquipmentId, a.ContractId }).Select(g => new { g.Key.EquipmentId, g.Key.ContractId, Amount = g.Sum(x => x.Amount) })
            .ToListAsync(ct);
        foreach (var e in equipment.Where(e => e.FixedAssetId != null))
        {
            var total = depreciation.GetValueOrDefault(e.FixedAssetId!.Value);
            var mine = allocations.Where(x => x.EquipmentId == e.Id).ToList();
            foreach (var x in mine) At(e.Id, x.ContractId).Depreciation += x.Amount;
            var rest = total - mine.Sum(x => x.Amount);
            if (rest != 0) At(e.Id, null).Depreciation += rest;
        }

        var contractIds = acc.Keys.Where(k => k.ContractId != null).Select(k => k.ContractId!.Value).Distinct().ToList();
        var contracts = await _db.Set<CommercialContract>().AsNoTracking().Where(c => contractIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => new { c.ContractNumber, Name = c.ProjectName ?? c.Title }, ct);

        var result = new EquipmentProfitabilityReportDto { From = start, To = end, ContractId = contractId };
        foreach (var ((equipmentId, contract), a) in acc.Where(x => x.Key.ContractId != null && (contractId == null || x.Key.ContractId == contractId)))
        {
            var c = contracts.GetValueOrDefault(contract!.Value);
            var row = ToRow(byId[equipmentId], a);
            row.ContractId = contract; row.ContractNumber = c?.ContractNumber; row.ProjectName = c?.Name;
            result.EquipmentProjects.Add(row);
        }
        result.EquipmentProjects = result.EquipmentProjects.OrderBy(r => r.ProjectName).ThenBy(r => r.Code).ToList();

        var inScope = contractId == null ? null : result.EquipmentProjects.Select(r => r.EquipmentId).ToHashSet();
        foreach (var group in acc.GroupBy(x => x.Key.EquipmentId).Where(g => inScope == null || inScope.Contains(g.Key)))
        {
            var total = new Acc();
            foreach (var x in group) total.Add(x.Value);
            var row = ToRow(byId[group.Key], total);
            row.UnallocatedCost = group.Where(x => x.Key.ContractId == null).Sum(x => x.Value.TotalCost);
            if (row.WorkHours != 0 || row.IdleHours != 0 || row.WorkUnits != 0 || row.TotalCost != 0 || row.OperationalRevenue != 0 || row.AccountingRevenue != 0)
                result.Equipment.Add(row);
        }
        result.Equipment = result.Equipment.OrderBy(r => r.Code).ToList();

        result.Projects = result.EquipmentProjects.GroupBy(r => r.ContractId!.Value).Select(g =>
        {
            var p = new EquipmentProjectProfitDto
            {
                ContractId = g.Key, ContractNumber = g.First().ContractNumber, ProjectName = g.First().ProjectName,
                EquipmentCount = g.Select(r => r.EquipmentId).Distinct().Count(), WorkHours = g.Sum(r => r.WorkHours),
                OperationalRevenue = g.Sum(r => r.OperationalRevenue), AccountingRevenue = g.Sum(r => r.AccountingRevenue), TotalCost = g.Sum(r => r.TotalCost),
            };
            p.OperationalProfit = p.OperationalRevenue - p.TotalCost; p.AccountingProfit = p.AccountingRevenue - p.TotalCost;
            p.OperationalMargin = Margin(p.OperationalProfit, p.OperationalRevenue); p.AccountingMargin = Margin(p.AccountingProfit, p.AccountingRevenue);
            return p;
        }).OrderBy(p => p.ProjectName).ToList();

        // الإجمالي: كل المعدات (بما فيه غير المنسوب لمشروع)، أو صفوف المشروع المختار وحده
        var basis = contractId == null ? result.Equipment : result.EquipmentProjects;
        result.Totals = new EquipmentProfitTotalsDto
        {
            OperationalRevenue = basis.Sum(r => r.OperationalRevenue), AccountingRevenue = basis.Sum(r => r.AccountingRevenue), TotalCost = basis.Sum(r => r.TotalCost),
            OperationalProfit = basis.Sum(r => r.OperationalProfit), AccountingProfit = basis.Sum(r => r.AccountingProfit),
            UncertifiedRevenue = basis.Sum(r => r.UncertifiedRevenue), UnpricedHours = basis.Sum(r => r.UnpricedHours),
            LossMaking = basis.Count(r => r.OperationalProfit < 0),
        };
        return result;
    }

    /// <summary>إيراد يوم عمل بأساس التسعيرة: الساعة والوردية بساعات الفوترة (تشغيل + توقف واستعداد بنسبتيهما، بحد أدنى يومي)،
    /// واليوم كاملاً إن عملت وإلا بنسبة التوقف أو الاستعداد، والرحلة والمهمة بوحداتها.</summary>
    internal static decimal DayRevenue(EquipmentRevenueRate rate, decimal hours, decimal idle, decimal standby, decimal units)
    {
        switch (rate.Basis)
        {
            case B.Trip or B.Task:
                return units * rate.Rate;
            case B.Day:
                if (hours > 0) return rate.Rate;
                var fraction = Math.Max(idle > 0 ? rate.IdleBillingPercent : 0, standby > 0 ? rate.StandbyBillingPercent : 0) / 100m;
                return rate.Rate * fraction;
            default:
                var billable = hours + idle * rate.IdleBillingPercent / 100m + standby * rate.StandbyBillingPercent / 100m;
                if (rate.MinimumDailyHours > 0 && hours > 0) billable = Math.Max(billable, rate.MinimumDailyHours);
                return rate.Basis == B.Shift ? billable / (rate.ShiftHours <= 0 ? 8 : rate.ShiftHours) * rate.Rate : billable * rate.Rate;
        }
    }

    private static decimal DayRevenue(EquipmentRevenueRate rate, WorkDay d) => DayRevenue(rate, d.Hours, d.Idle, d.Standby, d.Units);

    private static decimal? Margin(decimal profit, decimal revenue) => revenue > 0 ? Math.Round(profit * 100m / revenue, 1) : null;

    private static EquipmentProfitRowDto ToRow(Equipment e, Acc a)
    {
        var row = new EquipmentProfitRowDto
        {
            EquipmentId = e.Id, Code = e.Code, Name = e.NameAr, Category = e.Category, Ownership = e.Ownership, Bases = a.Bases.OrderBy(b => b).ToList(),
            WorkHours = a.WorkHours, IdleHours = a.IdleHours, WorkDays = a.WorkDays, WorkUnits = a.WorkUnits, UnpricedHours = a.UnpricedHours,
            OperationalRevenue = a.OperationalRevenue, CertifiedQuantity = a.CertifiedQuantity, AccountingRevenue = a.AccountingRevenue,
            FuelCost = a.FuelCost, MaintenanceCost = a.MaintenanceCost, Expenses = a.Expenses, OperatorCost = a.OperatorCost,
            Depreciation = a.Depreciation, RentalCost = a.RentalCost, TotalCost = a.TotalCost,
        };
        row.OperationalProfit = row.OperationalRevenue - row.TotalCost;
        row.AccountingProfit = row.AccountingRevenue - row.TotalCost;
        row.OperationalMargin = Margin(row.OperationalProfit, row.OperationalRevenue);
        row.AccountingMargin = Margin(row.AccountingProfit, row.AccountingRevenue);
        row.UncertifiedRevenue = row.OperationalRevenue - row.AccountingRevenue;
        return row;
    }
}
