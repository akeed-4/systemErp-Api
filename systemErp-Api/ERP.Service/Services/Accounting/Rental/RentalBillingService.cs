using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using CSt = ERP.Core.Models.Accounting.RentalChargeStatuses;
using K = ERP.Core.Models.Accounting.RentalChargeKinds;
using LSt = ERP.Core.Models.Accounting.RentalLineStatuses;
using T = ERP.Core.Models.Accounting.RentalTaxTreatments;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// رسوم التأجير وفوترته (R3a) عبر محرك فواتير المبيعات ومرتجعاته. القواعد:
/// <list type="bullet">
/// <item>إيجار السطر يُفوتر تراكمياً: تقدير الفترة من الخروج حتى نهاية الفوترة بأسعار السطر المثبّتة (والكسور بأسعار القائمة الأصغر) ناقص
/// المفوتر سابقاً، فيبقى الحد الأدنى والكسور صحيحة عبر الفترات. النهاية = الاستلام إن تم، وإلا لحظة الفوترة (لا مستقبلية إلا لعقد يُفوتر
/// مقدماً وضمن النهاية المخططة).</item>
/// <item>الرسوم: يدوية بنوعها، ومولّدة مرة لكل مصدر: الإضافي (ساعات العدّاد فوق المشمول × سعر الإضافي، خاضع)، والتأخير (أيام التأخر بعد
/// السماح × السعر اليومي × المعامل؛ تعويض يتطلب تصنيفاً، O6/D11)، والنقل المنفَّذ على العميل. لا يُفوتر إلا المعتمد، والنوع الذي يتطلب
/// تصنيفاً لا يُعتمد بلا تصنيف (خاضع أو خارج النطاق، O7). الإعفاء بسبب بالتفويض المقيد.</item>
/// <item>الفاتورة نظامية مرحّلة آجلة مرجعها <see cref="ReferenceType"/> (لا تُعدَّل ولا تُرتجع إلا بإلغاء الفوترة): الإيجار على حساب إيراد
/// التأجير بمركز تكلفة المعدة، والرسوم على حساب نوعها أو إيراد التأجير أو التعويضات. خارج النطاق بالفئة O وسبب VATEX-SA-OOS.</item>
/// <item>إلغاء آخر فوترة للعقد بلا تحصيل عليها: مرتجع كامل نظامي، ويعود المفوتر والرسوم كما كانت. الإغلاق بعد استلام كل سطر وفوترته
/// حتى استلامه وبلا رسوم معلّقة أو معتمدة.</item>
/// </list>
/// </summary>
public class RentalBillingService : IRentalBillingService
{
    public const string ReferenceType = "rental_billing";
    private const string Screen = PermissionCatalog.RentalBillingScreenId;
    private const decimal StandardVat = 15m;
    private const string OutOfScopeReason = "VATEX-SA-OOS";

    private readonly ErpDbContext _db;
    private readonly ISalesInvoiceService _sales;
    private readonly ISalesReturnService _returns;
    private readonly INumberSequenceService _numbers;
    private readonly ITransactionRunner _tx;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;
    private readonly IRentalContractService _contracts;
    private readonly IAuditService _audit;

    public RentalBillingService(ErpDbContext db, ISalesInvoiceService sales, ISalesReturnService returns, INumberSequenceService numbers, ITransactionRunner tx,
        ICurrentUser user, IPermissionService permissions, IRentalContractService contracts, IAuditService audit)
    {
        _db = db; _sales = sales; _returns = returns; _numbers = numbers; _tx = tx; _user = user; _permissions = permissions; _contracts = contracts; _audit = audit;
    }

    private string? By => _user.Name ?? _user.Email;

    // ---------- الإعداد ----------

    public async Task<RentalSettingsDto> SettingsAsync(CancellationToken ct = default)
    {
        var s = await _db.Set<RentalSettings>().AsNoTracking().FirstOrDefaultAsync(ct) ?? new RentalSettings();
        return new RentalSettingsDto
        {
            LateFeeFactor = s.LateFeeFactor, LateGraceHours = s.LateGraceHours, RequireDepositBeforeDelivery = s.RequireDepositBeforeDelivery,
            DefaultBillingMode = s.DefaultBillingMode, RequireTimesheetPhoto = s.RequireTimesheetPhoto,
        };
    }

    public async Task<RentalSettingsDto> SaveSettingsAsync(RentalSettingsDto r, CancellationToken ct = default)
    {
        if (r.LateFeeFactor < 0 || r.LateGraceHours < 0) throw new ValidationFailedException(Messages.WorkshopSettingsNegative);
        var mode = string.IsNullOrWhiteSpace(r.DefaultBillingMode) ? RentalBillingModes.Timesheet : r.DefaultBillingMode.Trim().ToLowerInvariant();
        if (!RentalBillingModes.IsValid(mode)) throw new ValidationFailedException(Messages.RentalBillingModeInvalid);
        var s = await _db.Set<RentalSettings>().FirstOrDefaultAsync(ct);
        if (s == null) { s = new RentalSettings(); _db.Add(s); }
        s.LateFeeFactor = decimal.Round(r.LateFeeFactor, 4); s.LateGraceHours = decimal.Round(r.LateGraceHours, 2); s.RequireDepositBeforeDelivery = r.RequireDepositBeforeDelivery;
        s.DefaultBillingMode = mode; s.RequireTimesheetPhoto = r.RequireTimesheetPhoto;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("RENTAL_SETTINGS_SAVED", nameof(RentalSettings), s.Id.ToString(), $"{s.LateFeeFactor} • {s.LateGraceHours}", ct);
        return await SettingsAsync(ct);
    }

    public async Task<List<RentalChargeTypeDto>> ChargeTypesAsync(CancellationToken ct = default)
    {
        await EnsureDefaultTypesAsync(ct);
        return (await _db.Set<RentalChargeType>().AsNoTracking().OrderBy(t => t.Code).ToListAsync(ct)).Select(TypeDto).ToList();
    }

    public async Task<RentalChargeTypeDto> SaveChargeTypeAsync(SaveRentalChargeTypeDto r, Guid? id, CancellationToken ct = default)
    {
        var kind = r.Kind?.Trim().ToLowerInvariant();
        if (!K.IsValid(kind)) throw new ValidationFailedException(Messages.RentalChargeKindInvalid);
        var treatment = string.IsNullOrWhiteSpace(r.DefaultTaxTreatment) ? T.Standard : r.DefaultTaxTreatment.Trim().ToLowerInvariant();
        if (!T.IsValid(treatment)) throw new ValidationFailedException(Messages.RentalTaxTreatmentInvalid);
        var name = Trim(r.NameAr, 150) ?? throw new ValidationFailedException(Messages.WorkshopNameRequired);
        if (Trim(r.RevenueAccountCode, 32) is string acc && !await _db.Set<Account>().AnyAsync(a => a.Code == acc && a.IsActive, ct))
            throw new ValidationFailedException(string.Format(Messages.RentalChargeAccountInvalid, acc));
        var t = id is Guid gid
            ? await _db.Set<RentalChargeType>().FirstOrDefaultAsync(x => x.Id == gid, ct) ?? throw new NotFoundException(Messages.RentalChargeTypeNotFound)
            : new RentalChargeType();
        var code = Trim(r.Code, 32)?.ToUpperInvariant() ?? (string.IsNullOrEmpty(t.Code) ? $"CH-{await _db.Set<RentalChargeType>().CountAsync(ct) + 1:00}" : t.Code);
        if (await _db.Set<RentalChargeType>().AnyAsync(x => x.Code == code && x.Id != t.Id, ct)) throw new ValidationFailedException(string.Format(Messages.WorkshopServiceCodeTaken, code));
        t.Code = code; t.NameAr = name; t.NameEn = Trim(r.NameEn, 150); t.Kind = kind!; t.RevenueAccountCode = Trim(r.RevenueAccountCode, 32);
        t.DefaultTaxTreatment = treatment; t.RequiresClassification = r.RequiresClassification; t.IsActive = r.IsActive;
        if (t.Id == Guid.Empty) _db.Add(t);
        await _db.SaveChangesAsync(ct);
        return TypeDto(t);
    }

    /// <summary>أنواع الرسوم الافتراضية لكل طبيعة (تُنشأ عند أول استخدام): التلف والتأخير تعويضات تتطلب تصنيفاً.</summary>
    private async Task EnsureDefaultTypesAsync(CancellationToken ct)
    {
        var existing = await _db.Set<RentalChargeType>().Select(t => t.Kind).Distinct().ToListAsync(ct);
        var defaults = new (string Kind, string Code, string Ar, string En)[]
        {
            (K.Overtime, "OVT", "ساعات تشغيل إضافية", "Overtime hours"), (K.Transport, "TRN", "نقل المعدات", "Equipment transport"),
            (K.Fuel, "FUL", "فرق الوقود", "Fuel difference"), (K.Damage, "DMG", "تعويض تلف", "Damage compensation"),
            (K.Late, "LTE", "غرامة تأخير", "Late return penalty"), (K.Cleaning, "CLN", "تنظيف", "Cleaning"), (K.Other, "OTH", "رسوم أخرى", "Other charges"),
        };
        var added = false;
        foreach (var d in defaults.Where(d => !existing.Contains(d.Kind)))
        {
            _db.Add(new RentalChargeType
            {
                Code = d.Code, NameAr = d.Ar, NameEn = d.En, Kind = d.Kind, DefaultTaxTreatment = T.Standard, RequiresClassification = K.IsCompensation(d.Kind),
            });
            added = true;
        }
        if (added) await _db.SaveChangesAsync(ct);
    }

    private async Task<RentalChargeType> TypeOfKindAsync(string kind, CancellationToken ct)
    {
        await EnsureDefaultTypesAsync(ct);
        return await _db.Set<RentalChargeType>().AsNoTracking().Where(t => t.Kind == kind).OrderByDescending(t => t.IsActive).ThenBy(t => t.Code).FirstAsync(ct);
    }

    // ---------- الرسوم ----------

    public async Task<List<RentalChargeDto>> ChargesAsync(Guid? contractId, string? status, CancellationToken ct = default)
    {
        var q = _db.Set<RentalCharge>().AsNoTracking();
        if (contractId is Guid c) q = q.Where(x => x.ContractId == c);
        if (!string.IsNullOrWhiteSpace(status)) { var s = status.Trim().ToLowerInvariant(); q = q.Where(x => x.Status == s); }
        return await ChargeDtosAsync(await q.OrderByDescending(x => x.CreatedAt).Take(1000).ToListAsync(ct), ct);
    }

    public async Task<RentalChargeDto> AddChargeAsync(SaveRentalChargeDto r, CancellationToken ct = default)
    {
        var c = await OpenContractAsync(r.ContractId, ct);
        if (r.LineId is Guid lid && !await _db.Set<RentalContractLine>().AnyAsync(l => l.Id == lid && l.ContractId == c.Id, ct))
            throw new ValidationFailedException(Messages.RentalHandoverLineNotFound);
        var type = await _db.Set<RentalChargeType>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == r.ChargeTypeId && t.IsActive, ct)
            ?? throw new ValidationFailedException(Messages.RentalChargeTypeNotFound);
        if (r.Quantity <= 0 || r.UnitPrice < 0) throw new ValidationFailedException(Messages.WorkOrderLineQuantityInvalid);
        var treatment = Treatment(r.TaxTreatment, type);
        var charge = new RentalCharge
        {
            ContractId = c.Id, LineId = r.LineId, ChargeTypeId = type.Id, Kind = type.Kind, Description = Trim(r.Description, 300) ?? type.NameAr,
            Quantity = decimal.Round(r.Quantity, 4), UnitPrice = decimal.Round(r.UnitPrice, 4), Amount = DocumentPricing.Round(r.Quantity * r.UnitPrice),
            TaxTreatment = treatment, SourceType = "manual", Status = CSt.Pending, Notes = Trim(r.Notes, 500), CreatedBy = By,
        };
        _db.Add(charge);
        await _db.SaveChangesAsync(ct);
        await LogAsync("RENTAL_CHARGE_ADDED", c, $"{charge.Description} • {charge.Amount:0.##}", ct);
        return (await ChargeDtosAsync(new List<RentalCharge> { charge }, ct)).Single();
    }

    public async Task<RentalChargeDto> ApproveChargeAsync(Guid id, ApproveRentalChargeDto r, CancellationToken ct = default)
    {
        var charge = await _db.Set<RentalCharge>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException(Messages.RentalChargeNotFound);
        if (charge.Status != CSt.Pending) throw new ConflictException(Messages.RentalChargeNotPending);
        var type = await _db.Set<RentalChargeType>().AsNoTracking().FirstAsync(t => t.Id == charge.ChargeTypeId, ct);
        if (!string.IsNullOrWhiteSpace(r.TaxTreatment)) charge.TaxTreatment = Treatment(r.TaxTreatment, type);
        // D11: التعويض والغرامة يُصنَّفان صراحة لكل رسم، ولا ضريبة تلقائية
        if (charge.TaxTreatment == null) throw new ValidationFailedException(Messages.RentalChargeNeedsClassification);
        charge.Status = CSt.Approved; charge.ApprovedBy = By;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("RENTAL_CHARGE_APPROVED", nameof(RentalCharge), id.ToString(), $"{charge.Description} • {charge.Amount:0.##} • {charge.TaxTreatment}", ct);
        return (await ChargeDtosAsync(new List<RentalCharge> { charge }, ct)).Single();
    }

    public async Task<RentalChargeDto> WaiveChargeAsync(Guid id, RentalReasonDto r, CancellationToken ct = default)
    {
        await _permissions.DemandAsync(PermissionCatalog.RentalChargeWaiverScreenId, ScreenAction.Approve, ct: ct);
        var charge = await _db.Set<RentalCharge>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException(Messages.RentalChargeNotFound);
        if (charge.Status is not (CSt.Pending or CSt.Approved)) throw new ConflictException(Messages.RentalChargeNotPending);
        charge.WaiveReason = Trim(r.Reason, 500) ?? throw new ValidationFailedException(Messages.RentalCancelReasonRequired);
        charge.Status = CSt.Waived; charge.WaivedBy = By;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("RENTAL_CHARGE_WAIVED", nameof(RentalCharge), id.ToString(), $"{charge.Description} • {charge.Amount:0.##} • {charge.WaiveReason}", ct);
        return (await ChargeDtosAsync(new List<RentalCharge> { charge }, ct)).Single();
    }

    public async Task DeleteChargeAsync(Guid id, CancellationToken ct = default)
    {
        var charge = await _db.Set<RentalCharge>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException(Messages.RentalChargeNotFound);
        if (charge.Status != CSt.Pending || charge.SourceType != "manual") throw new ConflictException(Messages.RentalChargeDeleteManualPending);
        _db.Remove(charge);
        await _db.SaveChangesAsync(ct);
    }

    public Task<List<RentalChargeDto>> GenerateChargesAsync(Guid contractId, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var c = await OpenContractAsync(contractId, token);
            var created = await GenerateAsync(c, token);
            if (created.Count > 0) await LogAsync("RENTAL_CHARGES_GENERATED", c, created.Count.ToString(), token);
            return await ChargeDtosAsync(created, token);
        }, ct);

    /// <summary>الإضافي والتأخير لكل سطر مستلم، والنقل المنفَّذ بمبلغ على العميل؛ المصدر الواحد لا يُولَّد مرتين.</summary>
    private async Task<List<RentalCharge>> GenerateAsync(RentalContract c, CancellationToken ct)
    {
        var existing = await _db.Set<RentalCharge>().AsNoTracking().Where(x => x.ContractId == c.Id && x.SourceId != null)
            .Select(x => new { x.SourceType, x.SourceId }).ToListAsync(ct);
        bool Has(string type, Guid id) => existing.Any(e => e.SourceType == type && e.SourceId == id);
        var settings = await _db.Set<RentalSettings>().AsNoTracking().FirstOrDefaultAsync(ct) ?? new RentalSettings();
        var created = new List<RentalCharge>();
        var codes = await _db.Set<Equipment>().AsNoTracking().Where(e => c.Lines.Select(l => l.EquipmentId).Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.Code, ct);

        foreach (var l in c.Lines.Where(l => l.Status == LSt.Returned && l.ActualOut != null && l.ActualIn != null))
        {
            var code = codes.GetValueOrDefault(l.EquipmentId);
            // الإضافي: ساعات العدّاد فوق المشمول لكل فترة من الأساس المختار (عقد كشوف الساعات: الإضافي من الكشوف نفسها)
            if (c.BillingMode != RentalBillingModes.Timesheet && !Has("overtime", l.Id) && l.IncludedHours > 0 && l.OvertimeRate > 0 && l.MeterOut is decimal mo && l.MeterIn is decimal mi)
            {
                var whole = RentalPricing.WholePeriods(l.ActualOut.Value, l.ActualIn.Value, l.Basis);
                var periods = Math.Max(1, RentalPricing.Advance(l.ActualOut.Value, l.Basis, whole) < l.ActualIn.Value ? whole + 1 : whole);
                var over = decimal.Round(mi - mo - l.IncludedHours * periods, 2);
                if (over > 0)
                {
                    var type = await TypeOfKindAsync(K.Overtime, ct);
                    created.Add(NewCharge(c, l, type, $"{type.NameAr} - {code} ({over:0.##})", over, l.OvertimeRate, T.Standard, CSt.Approved, "overtime", l.Id));
                }
            }
            // التأخير بعد السماح: أيام التأخر × السعر اليومي × المعامل (تعويض يُصنَّف عند اعتماده)
            var lateHours = (decimal)(l.ActualIn.Value - l.PlannedTo).TotalHours;
            if (!Has("late", l.Id) && settings.LateFeeFactor > 0 && lateHours > settings.LateGraceHours)
            {
                var days = Math.Ceiling(lateHours / 24m);
                var rates = await RatesAsync(l, ct);
                var dayRate = rates.TryGetValue(RentalRateBases.Day, out var day) ? day.Rate
                    : RentalPricing.Estimate(rates, l.Basis, l.PlannedTo, l.PlannedTo.AddDays(1), false).Rent;
                var price = decimal.Round(dayRate * settings.LateFeeFactor, 4);
                if (price > 0)
                {
                    var type = await TypeOfKindAsync(K.Late, ct);
                    created.Add(NewCharge(c, l, type, $"{type.NameAr} - {code} ({days:0} يوم)", days, price,
                        type.RequiresClassification ? null : type.DefaultTaxTreatment, CSt.Pending, "late", l.Id));
                }
            }
        }
        foreach (var job in await _db.Set<RentalTransportJob>().AsNoTracking()
                     .Where(j => j.ContractId == c.Id && j.Status == RentalTransportStatuses.Completed && j.ChargeAmount > 0).ToListAsync(ct))
        {
            if (Has("transport", job.Id)) continue;
            var type = await TypeOfKindAsync(K.Transport, ct);
            created.Add(NewCharge(c, null, type, $"{type.NameAr} {job.JobNumber}", 1, job.ChargeAmount, T.Standard, CSt.Approved, "transport", job.Id));
        }
        _db.AddRange(created);
        await _db.SaveChangesAsync(ct);
        return created;
    }

    private RentalCharge NewCharge(RentalContract c, RentalContractLine? l, RentalChargeType type, string description, decimal qty, decimal price, string? treatment,
        string status, string source, Guid sourceId) => new()
    {
        ContractId = c.Id, LineId = l?.Id, ChargeTypeId = type.Id, Kind = type.Kind, Description = description.Length > 300 ? description[..300] : description,
        Quantity = qty, UnitPrice = price, Amount = DocumentPricing.Round(qty * price), TaxTreatment = treatment, SourceType = source, SourceId = sourceId,
        Status = status, ApprovedBy = status == CSt.Approved ? "system" : null, CreatedBy = By,
    };

    private static string? Treatment(string? requested, RentalChargeType type)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var t = requested.Trim().ToLowerInvariant();
            if (!T.IsValid(t)) throw new ValidationFailedException(Messages.RentalTaxTreatmentInvalid);
            return t;
        }
        return type.RequiresClassification ? null : type.DefaultTaxTreatment;
    }

    // ---------- الفوترة ----------

    /// <summary>
    /// إيجار سطر في الفوترة. بالمدة: الفترة من آخر فوترة حتى نهايتها. بكشوف الساعات: الكشوف المعتمدة غير المفوترة (<see cref="Sheets"/>)،
    /// والفترة من أول يوم فيها حتى نهاية آخر يوم.
    /// </summary>
    private sealed record RentPart(RentalContractLine Line, DateTime From, DateTime To, decimal Amount, decimal Cumulative, string? Code, Guid? CostCenterId,
        List<RentalTimesheet>? Sheets = null);

    private async Task<(List<RentPart> Rent, List<RentalCharge> Charges, DateTime Until)> PlanAsync(RentalContract c, DateTime? requested, CancellationToken ct)
    {
        var now = SaudiTime.Now;
        var until = requested is DateTime u ? (u.Kind == DateTimeKind.Utc ? SaudiTime.FromUtc(u) : DateTime.SpecifyKind(u, DateTimeKind.Unspecified)) : now;
        var bySheets = c.BillingMode == RentalBillingModes.Timesheet;
        // الكشف لا يُعتمد لأيام مستقبلية فلا فوترة مقدمة بكشوف الساعات
        if (until > now.AddMinutes(15) && (!c.BillInAdvance || bySheets)) throw new ValidationFailedException(Messages.RentalBillingFutureNotAllowed);
        var equipment = await _db.Set<Equipment>().AsNoTracking().Where(e => c.Lines.Select(l => l.EquipmentId).Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => new { e.Code, e.CostCenterId }, ct);
        var rent = new List<RentPart>();
        if (bySheets)
        {
            // كل كشف معتمد غير مفوتر انتهى آخر يوم فيه قبل لحظة الفوترة
            var lastDay = until.Date.AddDays(until.TimeOfDay == TimeSpan.Zero ? -1 : 0);
            var sheets = await _db.Set<RentalTimesheet>().Where(t => t.ContractId == c.Id && t.Status == RentalTimesheetStatuses.Approved
                && t.BillingId == null && t.PeriodTo <= lastDay).OrderBy(t => t.PeriodFrom).ToListAsync(ct);
            foreach (var g in sheets.GroupBy(t => t.LineId))
            {
                var l = c.Lines.FirstOrDefault(x => x.Id == g.Key);
                if (l == null) continue;
                var list = g.ToList();
                var amount = list.Sum(t => t.Amount);
                var e = equipment.GetValueOrDefault(l.EquipmentId);
                rent.Add(new RentPart(l, list.Min(t => t.PeriodFrom), list.Max(t => t.PeriodTo).AddDays(1), amount, l.BilledAmount + amount, e?.Code, e?.CostCenterId, list));
            }
        }
        foreach (var l in c.Lines.Where(l => !bySheets && l.ActualOut != null && l.Status is LSt.Out or LSt.Returned).OrderBy(l => l.SortOrder))
        {
            // المستلم حتى استلامه؛ الخارج حتى لحظة الفوترة (أو مقدماً حتى نهايته المخططة)
            var cap = l.ActualIn ?? (c.BillInAdvance ? (l.PlannedTo > now ? l.PlannedTo : now) : now);
            var end = until < cap ? until : cap;
            var start = l.BilledThrough ?? l.ActualOut!.Value;
            if (end <= start) continue;
            var cumulative = RentalPricing.Estimate(await RatesAsync(l, ct), l.Basis, l.ActualOut!.Value, end, l.WithOperator).Total;
            var e = equipment.GetValueOrDefault(l.EquipmentId);
            rent.Add(new RentPart(l, start, end, Math.Max(0, cumulative - l.BilledAmount), cumulative, e?.Code, e?.CostCenterId));
        }
        var charges = await _db.Set<RentalCharge>().Where(x => x.ContractId == c.Id && x.Status == CSt.Approved && x.BillingId == null).ToListAsync(ct);
        return (rent, charges, until);
    }

    public async Task<RentalBillingPreviewDto> PreviewAsync(Guid contractId, DateTime? until, CancellationToken ct = default)
    {
        var c = await ContractAsync(contractId, track: false, ct);
        var (rent, charges, at) = await PlanAsync(c, until, ct);
        // بكشوف الساعات: سطر لكل كشف (وسطر لإضافيه) كما في الفاتورة
        var rentLines = rent.SelectMany(r => r.Sheets == null
            ? new[] { (Text: RentText(r), From: r.From, To: r.To, Amount: r.Amount, Line: r.Line.Id) }
            : r.Sheets.SelectMany(s => SheetItems(r, s).Select(i => (Text: i.Text, From: s.PeriodFrom, To: s.PeriodTo.AddDays(1), Amount: i.Amount, Line: r.Line.Id))).ToArray());
        var lines = rentLines.Where(r => r.Amount > 0).Select(r => new RentalBillingPreviewLineDto
        {
            ContractLineId = r.Line, Description = r.Text, PeriodFrom = r.From, PeriodTo = r.To, Amount = r.Amount, TaxTreatment = T.Standard,
            Vat = DocumentPricing.Round(r.Amount * StandardVat / 100m),
        }).Concat(charges.Select(x => new RentalBillingPreviewLineDto
        {
            ChargeId = x.Id, ContractLineId = x.LineId, Description = x.Description, Amount = x.Amount, TaxTreatment = x.TaxTreatment ?? T.Standard,
            Vat = x.TaxTreatment == T.OutOfScope ? 0 : DocumentPricing.Round(x.Amount * StandardVat / 100m),
        })).ToList();
        return new RentalBillingPreviewDto
        {
            ContractId = c.Id, Until = at, Lines = lines, Net = lines.Sum(l => l.Amount), Vat = lines.Sum(l => l.Vat), Total = lines.Sum(l => l.Amount + l.Vat),
            PendingCharges = await _db.Set<RentalCharge>().CountAsync(x => x.ContractId == c.Id && x.Status == CSt.Pending, ct),
        };
    }

    public Task<RentalBillingDto> BillAsync(Guid contractId, BillRentalContractDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            await LockContractAsync(contractId, token);
            var c = await ContractAsync(contractId, track: true, token);
            if (c.Status != RentalContractStatuses.Active) throw new ConflictException(Messages.RentalHandoverContractNotActive);
            var (rent, charges, until) = await PlanAsync(c, r.Until, token);
            if (rent.Count == 0 && charges.Count == 0) throw new ValidationFailedException(Messages.RentalNothingToBill);

            await ContractAccountsSetup.EnsureModuleMissingAsync(_db, ContractAccountsSetup.ModuleAccountSet.Rental, token);
            var s = await _db.Set<AccountingSettings>().AsNoTracking().FirstAsync(token);
            var revenue = s.RentalRevenueAccountCode ?? throw new ValidationFailedException(Messages.RentalAccountsMissing);
            var compensation = s.RentalCompensationIncomeAccountCode ?? revenue;
            var types = await _db.Set<RentalChargeType>().AsNoTracking().ToDictionaryAsync(t => t.Id, token);

            var billing = new RentalBilling { ContractId = c.Id, BilledUntil = until, Status = RentalBillingStatuses.Invoiced, CreatedBy = By };
            billing.BillingNumber = await _numbers.AssignAsync(new DocumentNumberRequest
            {
                Key = "rental_billing", DefaultPrefix = "RB-", WarehouseId = c.WarehouseId, Date = SaudiTime.Today,
                Exists = (n, tk) => _db.Set<RentalBilling>().AnyAsync(x => x.BillingNumber == n, tk),
            }, token);
            _db.Add(billing);

            var items = new List<InvoiceItemDto>();
            foreach (var p in rent)
            {
                billing.Lines.Add(new RentalBillingLine
                {
                    ContractLineId = p.Line.Id, PeriodFrom = p.From, PeriodTo = p.To, Amount = p.Amount, PreviousBilledThrough = p.Line.BilledThrough,
                    PreviousBilledAmount = p.Line.BilledAmount,
                });
                p.Line.BilledThrough = p.Line.BilledThrough is DateTime prev && prev > p.To ? prev : p.To;
                p.Line.BilledAmount = Math.Max(p.Line.BilledAmount, p.Cumulative);
                if (p.Sheets != null)
                {
                    foreach (var sheet in p.Sheets)
                    {
                        foreach (var (text, amount) in SheetItems(p, sheet).Where(i => i.Amount > 0))
                            items.Add(new InvoiceItemDto
                            {
                                ItemId = Guid.Empty, ItemName = text, Unit = "خدمة", Quantity = 1, UnitPrice = amount, VatRate = StandardVat,
                                RevenueAccountCode = revenue, CostCenterId = p.CostCenterId,
                            });
                        sheet.Status = RentalTimesheetStatuses.Billed; sheet.BillingId = billing.Id;
                    }
                }
                else if (p.Amount > 0)
                    items.Add(new InvoiceItemDto
                    {
                        ItemId = Guid.Empty, ItemName = RentText(p), Unit = "خدمة", Quantity = 1, UnitPrice = p.Amount, VatRate = StandardVat,
                        RevenueAccountCode = revenue, CostCenterId = p.CostCenterId,
                    });
            }
            foreach (var x in charges)
            {
                var type = types.GetValueOrDefault(x.ChargeTypeId);
                var account = type?.RevenueAccountCode ?? (K.IsCompensation(x.Kind) ? compensation : revenue);
                var outOfScope = x.TaxTreatment == T.OutOfScope;
                items.Add(new InvoiceItemDto
                {
                    ItemId = Guid.Empty, ItemName = x.Description, Unit = "خدمة", Quantity = x.Quantity, UnitPrice = x.UnitPrice, VatRate = outOfScope ? 0 : StandardVat,
                    VatCategory = outOfScope ? VatCategory.OutOfScope : null, VatExemptionReasonCode = outOfScope ? OutOfScopeReason : null, RevenueAccountCode = account,
                });
                x.Status = CSt.Invoiced; x.BillingId = billing.Id;
            }
            billing.RentAmount = rent.Sum(p => p.Amount); billing.ChargesAmount = charges.Sum(x => x.Amount);

            if (items.Count > 0)
            {
                var customer = await _db.Set<Customer>().AsNoTracking().FirstAsync(x => x.Id == c.CustomerId, token);
                var invoice = await _sales.CreateAsync(new CreateInvoiceDto
                {
                    SystemGenerated = true, Kind = InvoiceKind.Sales, InvoiceType = TradeHelper.InvoiceTypeFor(customer.VatNumber), IssueDate = SaudiTime.Today,
                    PartyId = customer.Id, PartyName = customer.NameAr, PartyVatNumber = customer.VatNumber, PartyPhone = customer.Phone, PartyEmail = customer.Email,
                    PaymentMethod = PaymentMethod.Credit, Status = "posted", WarehouseId = c.WarehouseId,
                    Notes = $"فوترة عقد التأجير {c.ContractNumber} حتى {until:yyyy-MM-dd HH:mm}{(string.IsNullOrWhiteSpace(r.Notes) ? string.Empty : " — " + r.Notes.Trim())}",
                    ReferenceType = ReferenceType, ReferenceId = billing.Id, ReferenceNumber = billing.BillingNumber, Items = items,
                }, token);
                billing.InvoiceId = invoice.Id; billing.InvoiceNumber = invoice.InvoiceNumber;
                billing.NetAmount = invoice.GrandTotal - invoice.VatTotal; billing.VatAmount = invoice.VatTotal; billing.TotalAmount = invoice.GrandTotal;
            }
            await _db.SaveChangesAsync(token);
            await LogAsync("RENTAL_BILLED", c, $"{billing.BillingNumber} • {billing.InvoiceNumber ?? "—"} • {billing.TotalAmount:0.##}", token);
            return (await BillingDtosAsync(new List<RentalBilling> { billing }, token)).Single();
        }, ct);

    public async Task<List<RentalBillingDto>> BillingsAsync(Guid? contractId, CancellationToken ct = default)
    {
        var q = _db.Set<RentalBilling>().AsNoTracking();
        if (contractId is Guid c) q = q.Where(b => b.ContractId == c);
        return await BillingDtosAsync(await q.OrderByDescending(b => b.CreatedAt).Take(1000).ToListAsync(ct), ct);
    }

    public Task<RentalBillingDto> CancelBillingAsync(Guid id, RentalReasonDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var b = await _db.Set<RentalBilling>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, token) ?? throw new NotFoundException(Messages.RentalBillingNotFound);
            if (b.Status != RentalBillingStatuses.Invoiced) throw new ConflictException(Messages.RentalBillingCancelled);
            await LockContractAsync(b.ContractId, token);
            var c = await ContractAsync(b.ContractId, track: true, token);
            if (c.Status != RentalContractStatuses.Active) throw new ConflictException(Messages.RentalContractNotEditable);
            if (await _db.Set<RentalBilling>().AnyAsync(x => x.ContractId == b.ContractId && x.Status == RentalBillingStatuses.Invoiced && x.CreatedAt > b.CreatedAt, token))
                throw new ConflictException(Messages.RentalBillingNotLatest);
            var reason = Trim(r.Reason, 500) ?? throw new ValidationFailedException(Messages.RentalCancelReasonRequired);
            var text = $"إلغاء فوترة التأجير {b.BillingNumber} ({b.InvoiceNumber}) - عقد {c.ContractNumber}: {reason}";
            if (b.InvoiceId is Guid invoiceId)
            {
                if (await _db.Set<VoucherAllocation>().AnyAsync(a => a.InvoiceId == invoiceId, token)) throw new ConflictException(Messages.WorkOrderInvoiceHasPayments);
                await _returns.CreateFromInvoiceAsync(new CreateReturnInvoiceRequestDto
                {
                    OriginalInvoiceId = invoiceId, ReturnReason = text, SystemGenerated = true, ReturnDate = SaudiTime.Today,
                }, token);
            }
            foreach (var bl in b.Lines)
                if (c.Lines.FirstOrDefault(l => l.Id == bl.ContractLineId) is { } line)
                {
                    line.BilledThrough = bl.PreviousBilledThrough; line.BilledAmount = bl.PreviousBilledAmount;
                }
            foreach (var x in await _db.Set<RentalCharge>().Where(x => x.BillingId == b.Id).ToListAsync(token))
            {
                x.Status = CSt.Approved; x.BillingId = null;
            }
            // كشوف الساعات المفوترة بها تعود معتمدة جاهزة لفوترة لاحقة
            foreach (var s in await _db.Set<RentalTimesheet>().Where(s => s.BillingId == b.Id).ToListAsync(token))
            {
                s.Status = RentalTimesheetStatuses.Approved; s.BillingId = null;
            }
            b.Status = RentalBillingStatuses.Cancelled; b.CancelReason = reason;
            await _db.SaveChangesAsync(token);
            await LogAsync("RENTAL_BILLING_CANCELLED", c, text, token);
            return (await BillingDtosAsync(new List<RentalBilling> { b }, token)).Single();
        }, ct);

    public Task<RentalContractDto> CloseContractAsync(Guid contractId, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            await LockContractAsync(contractId, token);
            var c = await ContractAsync(contractId, track: true, token);
            if (c.Status != RentalContractStatuses.Active) throw new ConflictException(Messages.RentalContractNotEditable);
            if (c.Lines.Any(l => l.Status is LSt.Pending or LSt.Out)) throw new ConflictException(Messages.RentalCloseLinesOpen);
            if (await _db.Set<RentalTimesheet>().AnyAsync(s => s.ContractId == c.Id && s.Status != RentalTimesheetStatuses.Billed, token))
                throw new ConflictException(Messages.RentalCloseTimesheetsOpen);
            if (c.Lines.Any(l => l.Status == LSt.Returned && (l.BilledThrough == null || l.BilledThrough < l.ActualIn))) throw new ConflictException(Messages.RentalCloseNotBilled);
            // ما لم يُولَّد بعد من الإضافي والتأخير والنقل يُولَّد الآن فيظهر قبل الإغلاق
            await GenerateAsync(c, token);
            if (await _db.Set<RentalCharge>().AnyAsync(x => x.ContractId == c.Id && (x.Status == CSt.Pending || x.Status == CSt.Approved), token))
                throw new ConflictException(Messages.RentalCloseChargesOpen);
            c.Status = RentalContractStatuses.Closed; c.ClosedAt = SaudiTime.Now;
            await _db.SaveChangesAsync(token);
            await LogAsync("RENTAL_CONTRACT_CLOSED", c, null, token);
            return await _contracts.GetAsync(c.Id, token);
        }, ct);

    // ---------- مساعدات ----------

    private async Task<Dictionary<string, RentalPricedBasis>> RatesAsync(RentalContractLine l, CancellationToken ct)
    {
        var rates = await RentalPricing.RatesAtAsync(_db, l.EquipmentId, null, l.PlannedFrom, ct);
        // سعر السطر المثبّت لأساسه، والكسور بأسعار القائمة الأصغر (كتقدير العقد)
        return new Dictionary<string, RentalPricedBasis>(rates)
        {
            [l.Basis] = new(l.Basis, l.Rate, l.WithOperator ? l.OperatorRate : rates.GetValueOrDefault(l.Basis)?.OperatorRate, l.IncludedHours, l.OvertimeRate, l.MinimumPeriods),
        };
    }

    private static string RentText(RentPart p) => $"إيجار {p.Code} من {p.From:yyyy-MM-dd HH:mm} إلى {p.To:yyyy-MM-dd HH:mm}";

    /// <summary>
    /// بنود الفاتورة من كشف ساعات: الإيجار (التشغيل والانتظار والمشغّل) بتفصيل الأيام والساعات، والإضافي بنداً مستقلاً بساعاته.
    /// </summary>
    private static IEnumerable<(string Text, decimal Amount)> SheetItems(RentPart p, RentalTimesheet s)
    {
        var detail = p.Line.Basis == RentalRateBases.Hour
            ? $"{s.BilledQuantity:0.##} ساعة"
            : $"{s.WorkDays:0} يوم تشغيل{(s.StandbyDays > 0 ? $" و{s.StandbyDays:0} انتظار" : string.Empty)}";
        var breakdown = s.BreakdownDays > 0 ? $"، عطل غير محتسب {s.BreakdownDays:0} يوم" : string.Empty;
        yield return ($"إيجار {p.Code} - كشف {s.TimesheetNumber} ({s.PeriodFrom:yyyy-MM-dd} إلى {s.PeriodTo:yyyy-MM-dd}): {detail}{breakdown}",
            s.RentAmount + s.StandbyAmount + s.OperatorAmount);
        if (s.OvertimeAmount > 0)
            yield return ($"ساعات إضافية {p.Code} - كشف {s.TimesheetNumber}: {s.OvertimeHours:0.##} ساعة", s.OvertimeAmount);
    }

    private Task LockContractAsync(Guid id, CancellationToken ct)
        => _db.Set<RentalContract>().Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, x => x.Status), ct);

    private async Task<RentalContract> ContractAsync(Guid id, bool track, CancellationToken ct)
    {
        var q = _db.Set<RentalContract>().Include(x => x.Lines).AsQueryable();
        if (!track) q = q.AsNoTracking();
        return await q.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException(Messages.RentalContractNotFound);
    }

    private async Task<RentalContract> OpenContractAsync(Guid id, CancellationToken ct)
    {
        var c = await ContractAsync(id, track: false, ct);
        if (c.Status is not (RentalContractStatuses.Draft or RentalContractStatuses.Active)) throw new ConflictException(Messages.RentalContractNotEditable);
        return c;
    }

    private async Task<List<RentalChargeDto>> ChargeDtosAsync(List<RentalCharge> list, CancellationToken ct)
    {
        if (list.Count == 0) return new();
        var contractIds = list.Select(x => x.ContractId).Distinct().ToList();
        var numbers = await _db.Set<RentalContract>().AsNoTracking().Where(c => contractIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.ContractNumber, ct);
        var lineIds = list.Where(x => x.LineId != null).Select(x => x.LineId!.Value).ToList();
        var codes = await (from l in _db.Set<RentalContractLine>().AsNoTracking()
                           join e in _db.Set<Equipment>().AsNoTracking() on l.EquipmentId equals e.Id
                           where lineIds.Contains(l.Id)
                           select new { l.Id, e.Code }).ToDictionaryAsync(x => x.Id, x => x.Code, ct);
        var types = await _db.Set<RentalChargeType>().AsNoTracking().ToDictionaryAsync(t => t.Id, ct);
        return list.Select(x => new RentalChargeDto
        {
            Id = x.Id, ContractId = x.ContractId, ContractNumber = numbers.GetValueOrDefault(x.ContractId), LineId = x.LineId,
            EquipmentCode = x.LineId is Guid lid ? codes.GetValueOrDefault(lid) : null, ChargeTypeId = x.ChargeTypeId,
            ChargeTypeName = types.GetValueOrDefault(x.ChargeTypeId)?.NameAr, Kind = x.Kind, Description = x.Description, Quantity = x.Quantity,
            UnitPrice = x.UnitPrice, Amount = x.Amount, TaxTreatment = x.TaxTreatment, RequiresClassification = types.GetValueOrDefault(x.ChargeTypeId)?.RequiresClassification ?? false,
            SourceType = x.SourceType, Status = x.Status, BillingId = x.BillingId, WaiveReason = x.WaiveReason, WaivedBy = x.WaivedBy, ApprovedBy = x.ApprovedBy,
            Notes = x.Notes, CreatedBy = x.CreatedBy, CreatedAt = x.CreatedAt,
        }).ToList();
    }

    private async Task<List<RentalBillingDto>> BillingDtosAsync(List<RentalBilling> list, CancellationToken ct)
    {
        var ids = list.Select(b => b.ContractId).Distinct().ToList();
        var contracts = await (from c in _db.Set<RentalContract>().AsNoTracking()
                               join cu in _db.Set<Customer>().AsNoTracking() on c.CustomerId equals cu.Id
                               where ids.Contains(c.Id)
                               select new { c.Id, c.ContractNumber, cu.NameAr }).ToDictionaryAsync(x => x.Id, ct);
        return list.Select(b => new RentalBillingDto
        {
            Id = b.Id, BillingNumber = b.BillingNumber, ContractId = b.ContractId, ContractNumber = contracts.GetValueOrDefault(b.ContractId)?.ContractNumber,
            CustomerName = contracts.GetValueOrDefault(b.ContractId)?.NameAr, BilledUntil = b.BilledUntil, InvoiceId = b.InvoiceId, InvoiceNumber = b.InvoiceNumber,
            RentAmount = b.RentAmount, ChargesAmount = b.ChargesAmount, NetAmount = b.NetAmount, VatAmount = b.VatAmount, TotalAmount = b.TotalAmount,
            Status = b.Status, CancelReason = b.CancelReason, CreatedBy = b.CreatedBy, CreatedAt = b.CreatedAt,
        }).ToList();
    }

    private static RentalChargeTypeDto TypeDto(RentalChargeType t) => new()
    {
        Id = t.Id, Code = t.Code, NameAr = t.NameAr, NameEn = t.NameEn, Kind = t.Kind, RevenueAccountCode = t.RevenueAccountCode,
        DefaultTaxTreatment = t.DefaultTaxTreatment, RequiresClassification = t.RequiresClassification, IsActive = t.IsActive,
    };

    private Task LogAsync(string action, RentalContract c, string? details, CancellationToken ct)
        => _audit.LogAsync(action, nameof(RentalContract), c.Id.ToString(), $"{c.ContractNumber}{(details == null ? string.Empty : " • " + details)}", ct);

    private static string? Trim(string? value, int max)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length > max ? v[..max] : v;
    }
}
