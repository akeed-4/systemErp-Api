using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using St = ERP.Core.Models.Accounting.RentalBookingStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// التوفر والحجوزات. الأوقات محلية بتوقيت المملكة (القادم بالتوقيت العالمي يُحوَّل). السعر والتقدير لقطة من الخادم عند كل حفظ
/// (<see cref="RentalPricing"/>)، والتعارض من مصدر واحد (<see cref="RentalAvailability"/>):
/// <list type="bullet">
/// <item>المسودة عرض سعر لا يحجز، ويقبل سطر فئة بعدد.</item>
/// <item>الحجز المؤقت (حتى موعد مستقبلي) والتأكيد يتطلبان معدة محددة لكل سطر، ويقفلان صفوفها ثم يرفضان أي تداخل مع إسناد
/// أو حجز آخر أو صيانة أو حالة أو خارج عقد المورد، ومع سطر آخر للمعدة نفسها في الحجز.</item>
/// <item>التعديل للمسودة والمؤقت (المؤقت يُعاد فحصه)، والمؤكد يُلغى ويُعاد حجزه؛ الحذف للمسودة؛ الإلغاء بسبب لغير المسودة.</item>
/// </list>
/// </summary>
public class RentalBookingService : IRentalBookingService
{
    private const int MaxLines = 100;
    private const int MaxCalendarDays = 400;

    private readonly ErpDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly ITransactionRunner _tx;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;

    public RentalBookingService(ErpDbContext db, INumberSequenceService numbers, ITransactionRunner tx, ICurrentUser user, IAuditService audit)
    {
        _db = db; _numbers = numbers; _tx = tx; _user = user; _audit = audit;
    }

    // ---------- التقويم والتقدير ----------

    public async Task<List<RentalCalendarRowDto>> CalendarAsync(DateTime from, DateTime to, Guid? categoryId, Guid? warehouseId, CancellationToken ct = default)
    {
        from = Local(from); to = Local(to);
        if (to <= from || (to - from).TotalDays > MaxCalendarDays) throw new ValidationFailedException(string.Format(Messages.RentalCalendarRangeInvalid, MaxCalendarDays));
        var categories = categoryId is Guid cat
            ? await _db.Set<RentalCategory>().AsNoTracking().Where(c => c.Id == cat || c.ParentId == cat).Select(c => c.Id).ToListAsync(ct)
            : null;
        var rows = await RentalSetupService.EquipmentRowsAsync(_db, e => e.IsRentable
            && (categories == null || (e.RentalCategoryId != null && categories.Contains(e.RentalCategoryId.Value)))
            && (warehouseId == null || e.HomeWarehouseId == warehouseId), ct);
        var blocks = await RentalAvailability.BlocksAsync(_db, rows.Select(r => r.Id).ToList(), from, to, null, SaudiTime.Now, ct);
        var byEquipment = blocks.ToLookup(b => b.EquipmentId);
        return rows.Select(r => new RentalCalendarRowDto { Equipment = r, Blocks = byEquipment[r.Id].ToList() }).ToList();
    }

    public async Task<RentalQuoteDto> QuoteAsync(RentalQuoteRequestDto request, CancellationToken ct = default)
    {
        var from = Local(request.From); var to = Local(request.To);
        var basis = request.Basis?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!RentalRateBases.IsValid(basis)) throw new ValidationFailedException(Messages.RentalBasisInvalid);
        if ((request.EquipmentId == null) == (request.CategoryId == null)) throw new ValidationFailedException(Messages.RentalLineTargetRequired);
        if (request.EquipmentId is Guid eq) await EnsureRentableAsync(eq, ct);
        else if (!await _db.Set<RentalCategory>().AnyAsync(c => c.Id == request.CategoryId, ct)) throw new ValidationFailedException(Messages.RentalCategoryNotFound);

        var rates = await RentalPricing.RatesAtAsync(_db, request.EquipmentId, request.CategoryId, from, ct);
        var estimate = RentalPricing.Estimate(rates, basis, from, to, request.WithOperator);
        var main = rates[basis];
        return new RentalQuoteDto
        {
            Rate = main.Rate, OperatorRate = main.OperatorRate ?? 0, Parts = estimate.Parts, RentAmount = estimate.Rent, OperatorAmount = estimate.Operator,
            Total = estimate.Total, MinimumApplied = estimate.MinimumApplied,
            Conflicts = request.EquipmentId is Guid id
                ? await RentalAvailability.BlocksAsync(_db, new[] { id }, from, to, request.ExcludeBookingId, SaudiTime.Now, ct)
                : new(),
        };
    }

    // ---------- الحجوزات ----------

    public async Task<List<RentalBookingDto>> ListAsync(string? status, Guid? customerId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var q = _db.Set<RentalBooking>().AsNoTracking().Include(b => b.Lines).AsQueryable();
        var now = SaudiTime.Now;
        if (!string.IsNullOrWhiteSpace(status))
        {
            var s = status.Trim().ToLowerInvariant();
            q = s == "expired"
                ? q.Where(b => b.Status == St.Held && (b.HoldUntil == null || b.HoldUntil <= now))
                : s == St.Held ? q.Where(b => b.Status == St.Held && b.HoldUntil > now) : q.Where(b => b.Status == s);
        }
        if (customerId is Guid c) q = q.Where(b => b.CustomerId == c);
        if (from is DateTime f) { var lf = Local(f); q = q.Where(b => b.Lines.Any(l => l.To > lf)); }
        if (to is DateTime t) { var lt = Local(t); q = q.Where(b => b.Lines.Any(l => l.From < lt)); }
        return await DtosAsync(await q.OrderByDescending(b => b.CreatedAt).Take(1000).ToListAsync(ct), ct);
    }

    public async Task<RentalBookingDto> GetAsync(Guid id, CancellationToken ct = default)
        => (await DtosAsync(new List<RentalBooking> { await FindAsync(id, track: false, ct) }, ct)).Single();

    public Task<RentalBookingDto> CreateAsync(SaveRentalBookingDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var b = new RentalBooking { Status = St.Draft, CreatedBy = _user.Name ?? _user.Email, CreatedByUserId = _user.UserId };
            await ApplyAsync(b, request, token);
            b.BookingNumber = await _numbers.AssignAsync(new DocumentNumberRequest
            {
                Key = "rental_booking", DefaultPrefix = "RBK-", WarehouseId = b.WarehouseId, Date = SaudiTime.Today,
                Exists = (n, c) => _db.Set<RentalBooking>().AnyAsync(x => x.BookingNumber == n, c),
            }, token);
            _db.Add(b);
            await _db.SaveChangesAsync(token);
            await LogAsync("RENTAL_BOOKING_CREATED", b, null, token);
            return await GetAsync(b.Id, token);
        }, ct);

    public Task<RentalBookingDto> UpdateAsync(Guid id, SaveRentalBookingDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var b = await FindAsync(id, track: true, token);
            if (b.Status is not (St.Draft or St.Held)) throw new ConflictException(Messages.RentalBookingNotEditable);
            await ApplyAsync(b, request, token);
            // المؤقت الساري يبقى حاجزاً: يُعاد فحص فتراته الجديدة
            if (b.Status == St.Held && b.HoldUntil > SaudiTime.Now) await EnsureBookableAsync(b, token);
            await _db.SaveChangesAsync(token);
            await LogAsync("RENTAL_BOOKING_UPDATED", b, null, token);
            return await GetAsync(id, token);
        }, ct);

    public Task<RentalBookingDto> HoldAsync(Guid id, HoldRentalBookingDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var b = await FindAsync(id, track: true, token);
            if (b.Status is not (St.Draft or St.Held)) throw new ConflictException(Messages.RentalBookingNotEditable);
            var until = Local(request.HoldUntil);
            if (until <= SaudiTime.Now) throw new ValidationFailedException(Messages.RentalHoldUntilInvalid);
            await EnsureBookableAsync(b, token);
            b.Status = St.Held; b.HoldUntil = until;
            await _db.SaveChangesAsync(token);
            await LogAsync("RENTAL_BOOKING_HELD", b, $"حتى {until:yyyy-MM-dd HH:mm}", token);
            return await GetAsync(id, token);
        }, ct);

    public Task<RentalBookingDto> ConfirmAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var b = await FindAsync(id, track: true, token);
            if (b.Status is not (St.Draft or St.Held)) throw new ConflictException(Messages.RentalBookingNotEditable);
            await EnsureCustomerAsync(b.CustomerId, token);
            await EnsureBookableAsync(b, token);
            b.Status = St.Confirmed; b.ConfirmedAt = DateTime.UtcNow; b.HoldUntil = null;
            await _db.SaveChangesAsync(token);
            await LogAsync("RENTAL_BOOKING_CONFIRMED", b, null, token);
            return await GetAsync(id, token);
        }, ct);

    public async Task<RentalBookingDto> CancelAsync(Guid id, RentalReasonDto request, CancellationToken ct = default)
    {
        var b = await FindAsync(id, track: true, ct);
        if (b.Status is St.Cancelled or St.Converted) throw new ConflictException(Messages.RentalBookingNotEditable);
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason)) throw new ValidationFailedException(Messages.RentalCancelReasonRequired);
        b.Status = St.Cancelled; b.CancelReason = reason.Length > 500 ? reason[..500] : reason;
        await _db.SaveChangesAsync(ct);
        await LogAsync("RENTAL_BOOKING_CANCELLED", b, b.CancelReason, ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var b = await FindAsync(id, track: true, ct);
        if (b.Status != St.Draft) throw new ConflictException(Messages.RentalBookingDeleteDraftOnly);
        _db.RemoveRange(b.Lines);
        _db.Remove(b);
        await _db.SaveChangesAsync(ct);
        await LogAsync("RENTAL_BOOKING_DELETED", b, null, ct);
    }

    // ---------- مساعدات ----------

    /// <summary>البيانات والأسطر ولقطات السعر (يستبدل الأسطر كلها).</summary>
    private async Task ApplyAsync(RentalBooking b, SaveRentalBookingDto r, CancellationToken ct)
    {
        await EnsureCustomerAsync(r.CustomerId, ct);
        if (r.WarehouseId is Guid wh && !await _db.Set<Warehouse>().AnyAsync(w => w.Id == wh, ct)) throw new ValidationFailedException(Messages.RentalWarehouseNotFound);
        if (r.Lines.Count == 0) throw new ValidationFailedException(Messages.RentalBookingLinesRequired);
        if (r.Lines.Count > MaxLines) throw new ValidationFailedException(string.Format(Messages.RentalBookingTooManyLines, MaxLines));

        var lines = new List<RentalBookingLine>();
        var order = 0;
        foreach (var l in r.Lines)
        {
            var from = Local(l.From); var to = Local(l.To);
            var basis = l.Basis?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!RentalRateBases.IsValid(basis)) throw new ValidationFailedException(Messages.RentalBasisInvalid);
            if ((l.EquipmentId == null) == (l.CategoryId == null)) throw new ValidationFailedException(Messages.RentalLineTargetRequired);
            var quantity = l.EquipmentId != null ? 1 : l.Quantity;
            if (quantity is < 1 or > 1000) throw new ValidationFailedException(Messages.RentalLineQuantityInvalid);
            if (l.EquipmentId is Guid eq) await EnsureRentableAsync(eq, ct);
            else if (!await _db.Set<RentalCategory>().AnyAsync(c => c.Id == l.CategoryId && c.IsActive, ct)) throw new ValidationFailedException(Messages.RentalCategoryNotFound);

            var rates = await RentalPricing.RatesAtAsync(_db, l.EquipmentId, l.CategoryId, from, ct);
            var estimate = RentalPricing.Estimate(rates, basis, from, to, l.WithOperator);
            lines.Add(new RentalBookingLine
            {
                EquipmentId = l.EquipmentId, CategoryId = l.EquipmentId == null ? l.CategoryId : null, Quantity = quantity, From = from, To = to,
                Basis = basis, WithOperator = l.WithOperator, Rate = rates[basis].Rate, OperatorRate = l.WithOperator ? rates[basis].OperatorRate ?? 0 : 0,
                EstimatedAmount = decimal.Round(estimate.Total * quantity, 2), SortOrder = ++order, Notes = Trim(l.Notes, 500),
            });
        }

        b.CustomerId = r.CustomerId; b.WarehouseId = r.WarehouseId; b.Notes = Trim(r.Notes, 1000);
        if (b.Lines.Count > 0) _db.RemoveRange(b.Lines.ToList());
        b.Lines.Clear();
        foreach (var line in lines) b.Lines.Add(line);
    }

    /// <summary>حجز فعلي: كل سطر بمعدة، لا تداخل بين أسطر المعدة نفسها، ولا تعارض بعد قفل صفوفها.</summary>
    private async Task EnsureBookableAsync(RentalBooking b, CancellationToken ct)
    {
        if (b.Lines.Any(l => l.EquipmentId == null)) throw new ValidationFailedException(Messages.RentalBookingNeedsEquipment);
        foreach (var group in b.Lines.GroupBy(l => l.EquipmentId!.Value))
        {
            var ordered = group.OrderBy(l => l.From).ToList();
            for (var i = 1; i < ordered.Count; i++)
                if (ordered[i].From < ordered[i - 1].To) throw new ValidationFailedException(Messages.RentalBookingSelfOverlap);
        }
        var ids = b.Lines.Select(l => l.EquipmentId!.Value).Distinct().OrderBy(x => x).ToList();
        await RentalAvailability.LockAsync(_db, ids, ct);
        var now = SaudiTime.Now;
        var problems = new List<string>();
        foreach (var line in b.Lines)
        {
            var conflicts = await RentalAvailability.BlocksAsync(_db, new[] { line.EquipmentId!.Value }, line.From, line.To, b.Id == Guid.Empty ? null : b.Id, now, ct);
            if (conflicts.Count == 0) continue;
            var code = await _db.Set<Equipment>().AsNoTracking().Where(e => e.Id == line.EquipmentId).Select(e => e.Code).FirstAsync(ct);
            foreach (var c in conflicts)
                problems.Add(string.Format(Messages.RentalEquipmentUnavailable, code, KindLabel(c.Kind), c.Reference ?? string.Empty, c.From,
                    c.To?.ToString("yyyy-MM-dd HH:mm") ?? "…"));
        }
        if (problems.Count > 0) throw new ConflictException(string.Join("\n", problems));
    }

    private static string KindLabel(string kind) => Messages.ResourceManager.GetString("RentalBlock_" + kind, Messages.Culture) ?? kind;

    private async Task EnsureRentableAsync(Guid equipmentId, CancellationToken ct)
    {
        var e = await _db.Set<Equipment>().AsNoTracking().Where(x => x.Id == equipmentId).Select(x => new { x.Code, x.IsRentable }).FirstOrDefaultAsync(ct)
            ?? throw new ValidationFailedException(Messages.RentalEquipmentNotFound);
        if (!e.IsRentable) throw new ValidationFailedException(string.Format(Messages.RentalEquipmentNotRentable, e.Code));
    }

    private async Task EnsureCustomerAsync(Guid customerId, CancellationToken ct)
    {
        var status = await _db.Set<Customer>().AsNoTracking().Where(c => c.Id == customerId).Select(c => c.Status).FirstOrDefaultAsync(ct)
            ?? throw new ValidationFailedException(Messages.RentalCustomerNotFound);
        if (status != "active") throw new ValidationFailedException(Messages.RentalCustomerInactive);
    }

    private async Task<RentalBooking> FindAsync(Guid id, bool track, CancellationToken ct)
    {
        var q = _db.Set<RentalBooking>().Include(b => b.Lines).AsQueryable();
        if (!track) q = q.AsNoTracking();
        return await q.FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException(Messages.RentalBookingNotFound);
    }

    private async Task<List<RentalBookingDto>> DtosAsync(List<RentalBooking> list, CancellationToken ct)
    {
        if (list.Count == 0) return new();
        var now = SaudiTime.Now;
        var customerIds = list.Select(b => b.CustomerId).Distinct().ToList();
        var customers = await _db.Set<Customer>().AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.NameAr, ct);
        var whIds = list.Where(b => b.WarehouseId != null).Select(b => b.WarehouseId!.Value).Distinct().ToList();
        var warehouses = await _db.Set<Warehouse>().AsNoTracking().Where(w => whIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.NameAr, ct);
        var lines = list.SelectMany(b => b.Lines).ToList();
        var eqIds = lines.Where(l => l.EquipmentId != null).Select(l => l.EquipmentId!.Value).Distinct().ToList();
        var equipment = await _db.Set<Equipment>().AsNoTracking().Where(e => eqIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => new { e.Code, e.NameAr }, ct);
        var catIds = lines.Where(l => l.CategoryId != null).Select(l => l.CategoryId!.Value).Distinct().ToList();
        var categories = await _db.Set<RentalCategory>().AsNoTracking().Where(c => catIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.NameAr, ct);

        return list.Select(b => new RentalBookingDto
        {
            Id = b.Id, BookingNumber = b.BookingNumber, CustomerId = b.CustomerId, CustomerName = customers.GetValueOrDefault(b.CustomerId),
            WarehouseId = b.WarehouseId, WarehouseName = b.WarehouseId is Guid w ? warehouses.GetValueOrDefault(w) : null,
            Status = b.Status, EffectiveStatus = b.Status == St.Held && !(b.HoldUntil > now) ? "expired" : b.Status,
            HoldUntil = b.HoldUntil, Notes = b.Notes, CancelReason = b.CancelReason, ConfirmedAt = b.ConfirmedAt, CreatedBy = b.CreatedBy, CreatedAt = b.CreatedAt,
            From = b.Lines.Count > 0 ? b.Lines.Min(l => l.From) : null, To = b.Lines.Count > 0 ? b.Lines.Max(l => l.To) : null,
            EstimatedTotal = b.Lines.Sum(l => l.EstimatedAmount),
            Lines = b.Lines.OrderBy(l => l.SortOrder).Select(l => new RentalBookingLineDto
            {
                Id = l.Id, EquipmentId = l.EquipmentId, CategoryId = l.CategoryId, Quantity = l.Quantity, From = l.From, To = l.To, Basis = l.Basis,
                WithOperator = l.WithOperator, Notes = l.Notes, Rate = l.Rate, OperatorRate = l.OperatorRate, EstimatedAmount = l.EstimatedAmount,
                EquipmentCode = l.EquipmentId is Guid e ? equipment.GetValueOrDefault(e)?.Code : null,
                EquipmentName = l.EquipmentId is Guid e2 ? equipment.GetValueOrDefault(e2)?.NameAr : null,
                CategoryName = l.CategoryId is Guid c ? categories.GetValueOrDefault(c) : null,
            }).ToList(),
        }).ToList();
    }

    private Task LogAsync(string action, RentalBooking b, string? details, CancellationToken ct)
        => _audit.LogAsync(action, nameof(RentalBooking), b.Id.ToString(),
            $"{b.BookingNumber} • {b.Status}{(details == null ? string.Empty : " • " + details)}", ct);

    /// <summary>الوقت المحلي بتوقيت المملكة؛ القادم بالتوقيت العالمي يُحوَّل، والبقية تُعامل محلية.</summary>
    private static DateTime Local(DateTime value) => value.Kind == DateTimeKind.Utc
        ? SaudiTime.FromUtc(value)
        : DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

    private static string? Trim(string? value, int max)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length > max ? v[..max] : v;
    }
}
