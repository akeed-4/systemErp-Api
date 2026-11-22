using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using DK = ERP.Core.Models.Accounting.RentalTimesheetDayKinds;
using LSt = ERP.Core.Models.Accounting.RentalLineStatuses;
using TSt = ERP.Core.Models.Accounting.RentalTimesheetStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// كشوف ساعات التأجير (T1). القواعد:
/// <list type="bullet">
/// <item>الكشف لسطر عقد ساري «يُفوتر بكشف الساعات» سُلِّمت معدته، وأيامه بين التسليم والاستلام (أو اليوم)، ولا يتداخل مع كشف قائم للسطر.
/// أيام الفترة كاملة: الناقص يُعبّأ (الجمعة إجازة، وغيرها تشغيل بالمشمول لليوم).</item>
/// <item>مسودة/مرفوض ← تقديم ← اعتماد بممثل العميل (صورة الكشف الموقّع مرفقة إن اشترطها الإعداد، والتوقيع اختياري) فتُجمَّد المبالغ بشروط
/// السطر (<see cref="RentalTimesheetPricing"/>) ← فوترة. الرفض بسبب يعيده للتعديل، وإعادة فتح المعتمد غير المفوتر بسبب.</item>
/// </list>
/// </summary>
public class RentalTimesheetService : IRentalTimesheetService
{
    private const int MaxDays = 62;
    private const int MaxSignatureChars = 400_000; // ≈ 300KB بعد ترميز base64

    private readonly ErpDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly ITransactionRunner _tx;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;

    public RentalTimesheetService(ErpDbContext db, INumberSequenceService numbers, ITransactionRunner tx, ICurrentUser user, IAuditService audit)
    {
        _db = db; _numbers = numbers; _tx = tx; _user = user; _audit = audit;
    }

    private string? By => _user.Name ?? _user.Email;

    // ---------- القراءة ----------

    public async Task<List<RentalTimesheetDto>> ListAsync(Guid? contractId, Guid? lineId, string? status, CancellationToken ct = default)
    {
        var q = _db.Set<RentalTimesheet>().AsNoTracking().Include(t => t.Days).AsQueryable();
        if (contractId is Guid c) q = q.Where(t => t.ContractId == c);
        if (lineId is Guid l) q = q.Where(t => t.LineId == l);
        if (!string.IsNullOrWhiteSpace(status)) { var s = status.Trim().ToLowerInvariant(); q = q.Where(t => t.Status == s); }
        var list = await q.OrderByDescending(t => t.PeriodFrom).ThenByDescending(t => t.CreatedAt).Take(1000).ToListAsync(ct);
        return await DtosAsync(list, includeSignature: false, ct);
    }

    public async Task<RentalTimesheetDto> GetAsync(Guid id, CancellationToken ct = default)
        => (await DtosAsync(new List<RentalTimesheet> { await LoadAsync(id, track: false, ct) }, includeSignature: true, ct)).Single();

    public async Task<RentalTimesheetDto> DraftAsync(Guid contractId, Guid lineId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var (c, line) = await LineAsync(contractId, lineId, ct);
        var (start, end) = Period(from, to);
        var sheet = new RentalTimesheet
        {
            ContractId = c.Id, LineId = line.Id, EquipmentId = line.EquipmentId, PeriodFrom = start, PeriodTo = end, Status = TSt.Draft,
            OperatorEmployeeId = line.OperatorEmployeeId,
        };
        foreach (var d in FillDays(line, start, end, new())) sheet.Days.Add(d);
        return (await DtosAsync(new List<RentalTimesheet> { sheet }, includeSignature: false, ct)).Single();
    }

    // ---------- الحفظ ----------

    public Task<RentalTimesheetDto> CreateAsync(SaveRentalTimesheetDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var (c, line) = await LineAsync(r.ContractId, r.LineId, token);
            var sheet = new RentalTimesheet { ContractId = c.Id, LineId = line.Id, EquipmentId = line.EquipmentId, Status = TSt.Draft, CreatedBy = By };
            await ApplyAsync(sheet, c, line, r, token);
            sheet.TimesheetNumber = await _numbers.AssignAsync(new DocumentNumberRequest
            {
                Key = "rental_timesheet", DefaultPrefix = "RTS-", WarehouseId = c.WarehouseId, Date = sheet.PeriodTo,
                Exists = (n, tk) => _db.Set<RentalTimesheet>().AnyAsync(x => x.TimesheetNumber == n, tk),
            }, token);
            _db.Add(sheet);
            await _db.SaveChangesAsync(token);
            await LogAsync("RENTAL_TIMESHEET_CREATED", sheet, $"{sheet.PeriodFrom:yyyy-MM-dd} → {sheet.PeriodTo:yyyy-MM-dd}", token);
            return await GetAsync(sheet.Id, token);
        }, ct);

    public Task<RentalTimesheetDto> UpdateAsync(Guid id, SaveRentalTimesheetDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var sheet = await LoadAsync(id, track: true, token);
            if (!TSt.Editable.Contains(sheet.Status)) throw new ConflictException(Messages.RentalTimesheetNotEditable);
            var (c, line) = await LineAsync(sheet.ContractId, sheet.LineId, token);
            _db.RemoveRange(sheet.Days);
            sheet.Days.Clear();
            await ApplyAsync(sheet, c, line, r, token);
            // المرفوض يعود مسودة بعد تصحيحه
            sheet.Status = TSt.Draft;
            await _db.SaveChangesAsync(token);
            await LogAsync("RENTAL_TIMESHEET_UPDATED", sheet, null, token);
            return await GetAsync(sheet.Id, token);
        }, ct);

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var sheet = await LoadAsync(id, track: true, ct);
        if (!TSt.Editable.Contains(sheet.Status)) throw new ConflictException(Messages.RentalTimesheetNotEditable);
        await DocumentAttachmentService.RemoveForOwnersAsync(_db, RentalAttachmentOwnerTypes.Timesheet, new[] { id }, ct);
        _db.Remove(sheet);
        await _db.SaveChangesAsync(ct);
        await LogAsync("RENTAL_TIMESHEET_DELETED", sheet, null, ct);
    }

    /// <summary>الفترة والأيام والمشغّل والملاحظات، بعد التحقق من الحدود والتداخل.</summary>
    private async Task ApplyAsync(RentalTimesheet sheet, RentalContract c, RentalContractLine line, SaveRentalTimesheetDto r, CancellationToken ct)
    {
        var (start, end) = Period(r.PeriodFrom, r.PeriodTo);
        var first = line.ActualOut!.Value.Date;
        var last = (line.ActualIn ?? SaudiTime.Now).Date;
        if (start < first || end > last) throw new ValidationFailedException(string.Format(Messages.RentalTimesheetOutsideRental, first, last));
        var overlap = await _db.Set<RentalTimesheet>().AsNoTracking()
            .Where(t => t.LineId == line.Id && t.Id != sheet.Id && TSt.Occupying.Contains(t.Status) && t.PeriodFrom <= end && t.PeriodTo >= start)
            .Select(t => t.TimesheetNumber).FirstOrDefaultAsync(ct);
        if (overlap != null) throw new ConflictException(string.Format(Messages.RentalTimesheetOverlap, overlap));
        if (r.OperatorEmployeeId is Guid op && !await _db.Set<Employee>().AnyAsync(e => e.Id == op, ct))
            throw new ValidationFailedException(Messages.RentalOperatorNotFound);

        var given = new Dictionary<DateTime, SaveRentalTimesheetDayDto>();
        foreach (var d in r.Days)
        {
            var date = d.Date.Date;
            var kind = d.Kind?.Trim().ToLowerInvariant();
            var hours = new[] { d.WorkedHours, d.StandbyHours, d.BreakdownHours };
            if (date < start || date > end || !DK.IsValid(kind) || hours.Any(h => h < 0) || hours.Sum() > 24 || given.ContainsKey(date)
                || (d.MeterStart is decimal ms && d.MeterEnd is decimal me && me < ms))
                throw new ValidationFailedException(string.Format(Messages.RentalTimesheetDayInvalid, date));
            given[date] = d;
        }
        sheet.PeriodFrom = start; sheet.PeriodTo = end;
        sheet.OperatorEmployeeId = r.OperatorEmployeeId ?? line.OperatorEmployeeId;
        sheet.Notes = Trim(r.Notes, 1000);
        foreach (var day in FillDays(line, start, end, given)) sheet.Days.Add(day);
    }

    /// <summary>أيام الفترة كاملة: المُدخل كما هو (التشغيل وحده يحمل ساعات تشغيل)، والناقص الجمعة إجازة وغيرها تشغيل بالمشمول لليوم.</summary>
    private static IEnumerable<RentalTimesheetDay> FillDays(RentalContractLine line, DateTime start, DateTime end, Dictionary<DateTime, SaveRentalTimesheetDayDto> given)
    {
        var standard = decimal.Round(RentalTimesheetPricing.IncludedPerDay(RentalTimesheetPricing.TermsOf(line)), 2);
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (given.TryGetValue(date, out var d))
            {
                var kind = d.Kind.Trim().ToLowerInvariant();
                yield return new RentalTimesheetDay
                {
                    Date = date, Kind = kind, WorkedHours = kind == DK.Work ? decimal.Round(d.WorkedHours, 2) : 0m,
                    StandbyHours = decimal.Round(d.StandbyHours, 2), BreakdownHours = decimal.Round(d.BreakdownHours, 2),
                    MeterStart = d.MeterStart, MeterEnd = d.MeterEnd, Notes = Trim(d.Notes, 300),
                };
                continue;
            }
            var friday = date.DayOfWeek == DayOfWeek.Friday;
            yield return new RentalTimesheetDay { Date = date, Kind = friday ? DK.Off : DK.Work, WorkedHours = friday ? 0m : standard };
        }
    }

    // ---------- دورة الاعتماد ----------

    public async Task<RentalTimesheetDto> SubmitAsync(Guid id, CancellationToken ct = default)
    {
        var sheet = await LoadAsync(id, track: true, ct);
        if (sheet.Status != TSt.Draft) throw new ConflictException(string.Format(Messages.RentalTimesheetStatusInvalid, sheet.Status));
        sheet.Status = TSt.Submitted; sheet.SubmittedBy = By; sheet.SubmittedAt = SaudiTime.Now; sheet.RejectReason = null;
        await _db.SaveChangesAsync(ct);
        await LogAsync("RENTAL_TIMESHEET_SUBMITTED", sheet, null, ct);
        return await GetAsync(id, ct);
    }

    public Task<RentalTimesheetDto> ApproveAsync(Guid id, ApproveRentalTimesheetDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var sheet = await LoadAsync(id, track: true, token);
            if (sheet.Status is not (TSt.Draft or TSt.Submitted)) throw new ConflictException(string.Format(Messages.RentalTimesheetStatusInvalid, sheet.Status));
            var approver = Trim(r.ClientApproverName, 200) ?? throw new ValidationFailedException(Messages.RentalTimesheetApproverRequired);
            var signature = string.IsNullOrWhiteSpace(r.ClientSignature) ? null : r.ClientSignature.Trim();
            if (signature != null && (signature.Length > MaxSignatureChars
                    || !(signature.StartsWith("data:image/png;base64,", StringComparison.OrdinalIgnoreCase) || signature.StartsWith("data:image/jpeg;base64,", StringComparison.OrdinalIgnoreCase))))
                throw new ValidationFailedException(Messages.RentalTimesheetSignatureInvalid);
            var settings = await _db.Set<RentalSettings>().AsNoTracking().FirstOrDefaultAsync(token) ?? new RentalSettings();
            if (settings.RequireTimesheetPhoto
                && !await _db.Set<DocumentAttachment>().AnyAsync(a => a.OwnerType == RentalAttachmentOwnerTypes.Timesheet && a.OwnerId == id, token))
                throw new ValidationFailedException(Messages.RentalTimesheetPhotoRequired);

            // المبالغ تُجمَّد بشروط السطر الحالية (لا تتغير بعد ذلك بتعديل العقد)
            var (_, line) = await LineAsync(sheet.ContractId, sheet.LineId, token);
            Freeze(sheet, Price(line, sheet.Days));
            sheet.ClientApproverName = approver; sheet.ClientApproverTitle = Trim(r.ClientApproverTitle, 150); sheet.ClientSignature = signature;
            sheet.ClientApprovedAt = r.ClientApprovedAt ?? SaudiTime.Now;
            sheet.Status = TSt.Approved; sheet.ApprovedBy = By; sheet.ApprovedAt = SaudiTime.Now; sheet.RejectReason = null;
            await _db.SaveChangesAsync(token);
            await LogAsync("RENTAL_TIMESHEET_APPROVED", sheet, $"{approver} • {sheet.Amount:0.##}", token);
            return await GetAsync(id, token);
        }, ct);

    public async Task<RentalTimesheetDto> RejectAsync(Guid id, RentalReasonDto r, CancellationToken ct = default)
    {
        var sheet = await LoadAsync(id, track: true, ct);
        if (sheet.Status != TSt.Submitted) throw new ConflictException(string.Format(Messages.RentalTimesheetStatusInvalid, sheet.Status));
        sheet.RejectReason = Trim(r.Reason, 500) ?? throw new ValidationFailedException(Messages.RentalCancelReasonRequired);
        sheet.Status = TSt.Rejected;
        await _db.SaveChangesAsync(ct);
        await LogAsync("RENTAL_TIMESHEET_REJECTED", sheet, sheet.RejectReason, ct);
        return await GetAsync(id, ct);
    }

    public async Task<RentalTimesheetDto> ReopenAsync(Guid id, RentalReasonDto r, CancellationToken ct = default)
    {
        var sheet = await LoadAsync(id, track: true, ct);
        if (sheet.Status == TSt.Billed || sheet.BillingId != null) throw new ConflictException(Messages.RentalTimesheetBilled);
        if (sheet.Status != TSt.Approved) throw new ConflictException(string.Format(Messages.RentalTimesheetStatusInvalid, sheet.Status));
        var reason = Trim(r.Reason, 500) ?? throw new ValidationFailedException(Messages.RentalCancelReasonRequired);
        sheet.Status = TSt.Draft; sheet.ApprovedBy = null; sheet.ApprovedAt = null;
        await _db.SaveChangesAsync(ct);
        await LogAsync("RENTAL_TIMESHEET_REOPENED", sheet, reason, ct);
        return await GetAsync(id, ct);
    }

    // ---------- التسعير ----------

    internal static RentalTimesheetTotals Price(RentalContractLine line, IEnumerable<RentalTimesheetDay> days)
        => RentalTimesheetPricing.Compute(RentalTimesheetPricing.TermsOf(line),
            days.Select(d => new RentalTimesheetDayInput(d.Date, d.Kind, d.WorkedHours, d.StandbyHours, d.BreakdownHours)).ToList());

    private static void Freeze(RentalTimesheet s, RentalTimesheetTotals t)
    {
        s.WorkDays = t.WorkDays; s.StandbyDays = t.StandbyDays; s.BreakdownDays = t.BreakdownDays;
        s.WorkedHours = t.WorkedHours; s.StandbyHours = t.StandbyHours; s.BreakdownHours = t.BreakdownHours;
        s.BilledQuantity = t.BilledQuantity; s.OvertimeHours = t.OvertimeHours;
        s.RentAmount = t.RentAmount; s.StandbyAmount = t.StandbyAmount; s.OvertimeAmount = t.OvertimeAmount; s.OperatorAmount = t.OperatorAmount;
        s.Amount = t.Amount;
    }

    // ---------- مساعدات ----------

    private static (DateTime Start, DateTime End) Period(DateTime from, DateTime to)
    {
        var start = from.Date; var end = to.Date;
        if (end < start || (end - start).TotalDays >= MaxDays) throw new ValidationFailedException(Messages.RentalTimesheetPeriodInvalid);
        return (start, end);
    }

    private async Task<(RentalContract Contract, RentalContractLine Line)> LineAsync(Guid contractId, Guid lineId, CancellationToken ct)
    {
        var c = await _db.Set<RentalContract>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == contractId, ct)
            ?? throw new NotFoundException(Messages.RentalContractNotFound);
        if (c.Status != RentalContractStatuses.Active) throw new ConflictException(Messages.RentalHandoverContractNotActive);
        if (c.BillingMode != RentalBillingModes.Timesheet) throw new ConflictException(Messages.RentalTimesheetModeRequired);
        var line = c.Lines.FirstOrDefault(l => l.Id == lineId) ?? throw new NotFoundException(Messages.RentalHandoverLineNotFound);
        if (line.ActualOut == null || line.Status is not (LSt.Out or LSt.Returned)) throw new ConflictException(Messages.RentalTimesheetLineNotRented);
        return (c, line);
    }

    private async Task<RentalTimesheet> LoadAsync(Guid id, bool track, CancellationToken ct)
    {
        var q = _db.Set<RentalTimesheet>().Include(t => t.Days).AsQueryable();
        if (!track) q = q.AsNoTracking();
        return await q.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException(Messages.RentalTimesheetNotFound);
    }

    private async Task<List<RentalTimesheetDto>> DtosAsync(List<RentalTimesheet> list, bool includeSignature, CancellationToken ct)
    {
        if (list.Count == 0) return new();
        var contractIds = list.Select(t => t.ContractId).Distinct().ToList();
        var contracts = await (from c in _db.Set<RentalContract>().AsNoTracking()
                               join cu in _db.Set<Customer>().AsNoTracking() on c.CustomerId equals cu.Id
                               where contractIds.Contains(c.Id)
                               select new { c.Id, c.ContractNumber, cu.NameAr }).ToDictionaryAsync(x => x.Id, ct);
        var lineIds = list.Select(t => t.LineId).Distinct().ToList();
        var lines = await _db.Set<RentalContractLine>().AsNoTracking().Where(l => lineIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        var equipmentIds = list.Select(t => t.EquipmentId).Distinct().ToList();
        var equipment = await _db.Set<Equipment>().AsNoTracking().Where(e => equipmentIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => new { e.Code, e.NameAr }, ct);
        var operatorIds = list.Where(t => t.OperatorEmployeeId != null).Select(t => t.OperatorEmployeeId!.Value).Distinct().ToList();
        var operators = await _db.Set<Employee>().AsNoTracking().Where(e => operatorIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.NameAr, ct);
        var ids = list.Select(t => t.Id).ToList();
        var photos = await _db.Set<DocumentAttachment>().AsNoTracking()
            .Where(a => a.OwnerType == RentalAttachmentOwnerTypes.Timesheet && ids.Contains(a.OwnerId))
            .GroupBy(a => a.OwnerId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var billingIds = list.Where(t => t.BillingId != null).Select(t => t.BillingId!.Value).Distinct().ToList();
        var billings = await _db.Set<RentalBilling>().AsNoTracking().Where(b => billingIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.BillingNumber, ct);

        return list.Select(t =>
        {
            var line = lines.GetValueOrDefault(t.LineId);
            var frozen = t.Status is TSt.Approved or TSt.Billed;
            // المسودة تُسعَّر الآن بشروط السطر؛ المعتمد كما جُمِّد
            var totals = frozen || line == null ? null : Price(line, t.Days);
            var terms = line == null ? null : RentalTimesheetPricing.TermsOf(line);
            var contract = contracts.GetValueOrDefault(t.ContractId);
            var eq = equipment.GetValueOrDefault(t.EquipmentId);
            return new RentalTimesheetDto
            {
                Id = t.Id, TimesheetNumber = t.TimesheetNumber, ContractId = t.ContractId, ContractNumber = contract?.ContractNumber, CustomerName = contract?.NameAr,
                LineId = t.LineId, EquipmentId = t.EquipmentId, EquipmentCode = eq?.Code, EquipmentName = eq?.NameAr,
                PeriodFrom = t.PeriodFrom, PeriodTo = t.PeriodTo, Status = t.Status,
                OperatorEmployeeId = t.OperatorEmployeeId, OperatorName = t.OperatorEmployeeId is Guid op ? operators.GetValueOrDefault(op) : null,
                Basis = line?.Basis ?? string.Empty, Rate = line?.Rate ?? 0, IncludedHoursPerDay = terms == null ? 0 : decimal.Round(RentalTimesheetPricing.IncludedPerDay(terms), 2),
                DailyMinimumHours = line?.DailyMinimumHours ?? 0, StandbyPercent = line?.StandbyPercent ?? 100,
                ClientApproverName = t.ClientApproverName, ClientApproverTitle = t.ClientApproverTitle, HasSignature = t.ClientSignature != null,
                ClientSignature = includeSignature ? t.ClientSignature : null, ClientApprovedAt = t.ClientApprovedAt,
                WorkDays = totals?.WorkDays ?? t.WorkDays, StandbyDays = totals?.StandbyDays ?? t.StandbyDays, BreakdownDays = totals?.BreakdownDays ?? t.BreakdownDays,
                WorkedHours = totals?.WorkedHours ?? t.WorkedHours, StandbyHours = totals?.StandbyHours ?? t.StandbyHours, BreakdownHours = totals?.BreakdownHours ?? t.BreakdownHours,
                BilledQuantity = totals?.BilledQuantity ?? t.BilledQuantity, OvertimeHours = totals?.OvertimeHours ?? t.OvertimeHours,
                RentAmount = totals?.RentAmount ?? t.RentAmount, StandbyAmount = totals?.StandbyAmount ?? t.StandbyAmount,
                OvertimeAmount = totals?.OvertimeAmount ?? t.OvertimeAmount, OperatorAmount = totals?.OperatorAmount ?? t.OperatorAmount,
                Amount = totals?.Amount ?? t.Amount, AmountsFrozen = frozen,
                PhotoCount = photos.GetValueOrDefault(t.Id), BillingId = t.BillingId, BillingNumber = t.BillingId is Guid b ? billings.GetValueOrDefault(b) : null,
                RejectReason = t.RejectReason, SubmittedBy = t.SubmittedBy, SubmittedAt = t.SubmittedAt, ApprovedBy = t.ApprovedBy, ApprovedAt = t.ApprovedAt,
                Notes = t.Notes, CreatedBy = t.CreatedBy, CreatedAt = t.CreatedAt,
                Days = t.Days.OrderBy(d => d.Date).Select(d => new RentalTimesheetDayDto
                {
                    Id = d.Id, Date = d.Date, Kind = d.Kind, WorkedHours = d.WorkedHours, StandbyHours = d.StandbyHours, BreakdownHours = d.BreakdownHours,
                    MeterStart = d.MeterStart, MeterEnd = d.MeterEnd, Notes = d.Notes,
                }).ToList(),
            };
        }).ToList();
    }

    private Task LogAsync(string action, RentalTimesheet t, string? details, CancellationToken ct)
        => _audit.LogAsync(action, nameof(RentalTimesheet), t.Id.ToString(), $"{t.TimesheetNumber}{(details == null ? string.Empty : " • " + details)}", ct);

    private static string? Trim(string? value, int max)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length > max ? v[..max] : v;
    }
}
