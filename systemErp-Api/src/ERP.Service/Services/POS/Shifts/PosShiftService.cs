using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.POS;
using ERP.Core.DTOs.POS;
using ERP.Service.Data;
using ERP.Service.Services.Accounting;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.POS;

public class PosShiftService : IPosShiftService
{
    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;
    private readonly INumberSequenceService _numbers;
    private readonly ITransactionRunner _tx;
    private readonly IPosSaleService _sales;
    private readonly IPosSaleReturnService _returns;
    private readonly IAccountingPostingService _posting;

    public PosShiftService(ErpDbContext db, ICurrentUser user, INumberSequenceService numbers, ITransactionRunner tx,
        IPosSaleService sales, IPosSaleReturnService returns, IAccountingPostingService posting)
    {
        _db = db; _user = user; _numbers = numbers; _tx = tx; _sales = sales; _returns = returns; _posting = posting;
    }

    private Guid Me => _user.UserId ?? throw new UnauthorizedAppException();

    public async Task<PosShiftDto?> GetActiveAsync(CancellationToken ct = default)
    {
        var me = Me;
        var shift = await _db.Set<PosShift>().AsNoTracking().FirstOrDefaultAsync(s => s.CashierId == me && s.Status == PosShiftStatus.Open, ct);
        return shift == null ? null : Mapper.Map<PosShiftDto>(shift);
    }

    public Task<PosShiftDto> OpenAsync(OpenShiftRequestDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var me = Me;
            if (r.OpeningCash < 0) throw new ValidationFailedException("الرصيد الافتتاحي لا يكون سالباً.");
            if (string.IsNullOrWhiteSpace(r.PosTerminalName)) throw new ValidationFailedException("اسم الجهاز/الكاشير مطلوب.");
            if (await _db.Set<PosShift>().AnyAsync(s => s.CashierId == me && s.Status == PosShiftStatus.Open, token))
                throw new ConflictException("لديك وردية مفتوحة بالفعل؛ أغلقها أولاً.");
            if (await _db.Set<PosShift>().AnyAsync(s => s.PosTerminalName == r.PosTerminalName && s.Status == PosShiftStatus.Open, token))
                throw new ConflictException("هذا الجهاز عليه وردية مفتوحة لكاشير آخر.");

            var shift = new PosShift
            {
                ShiftNumber = await _numbers.NextAsync("pos_shift", "SHF-", token),
                CashierId = me, CashierName = _user.Name ?? string.Empty, PosTerminalName = r.PosTerminalName.Trim(),
                OpenedAt = DateTime.UtcNow, OpeningCash = r.OpeningCash, Status = PosShiftStatus.Open,
            };
            _db.Add(shift);
            await _db.SaveChangesAsync(token);
            return Mapper.Map<PosShiftDto>(shift);
        }, ct);

    /// <summary>إغلاق الوردية: الجرد بالفئات (إن أُرسل) يحدد النقد الفعلي، ويُرحَّل قيد العجز/الزيادة.</summary>
    public Task<PosShiftDto> CloseAsync(CloseShiftRequestDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var me = Me;
            var actual = r.ClosingCashActual;
            if (r.Denominations.Count > 0)
            {
                if (r.Denominations.Any(d => d.Value <= 0 || d.Count < 0)) throw new ValidationFailedException("الفئات موجبة والأعداد غير سالبة.");
                actual = DocumentPricing.Round(r.Denominations.Sum(d => d.Value * d.Count));
                if (r.ClosingCashActual != 0 && r.ClosingCashActual != actual)
                    throw new ValidationFailedException($"مجموع الفئات ({actual:0.00}) لا يطابق النقد الفعلي المُدخل ({r.ClosingCashActual:0.00}).");
            }
            if (actual < 0) throw new ValidationFailedException("النقد الفعلي لا يكون سالباً.");
            var shift = await _db.Set<PosShift>().FirstOrDefaultAsync(s => s.CashierId == me && s.Status == PosShiftStatus.Open, token)
                ?? throw new NotFoundException("لا توجد وردية مفتوحة لإغلاقها.");

            shift.ClosedAt = DateTime.UtcNow; shift.Status = PosShiftStatus.Closed;
            shift.ClosingCashActual = actual;
            shift.ClosingNotes = r.ClosingNotes;
            shift.ClosingDenominations = r.Denominations.Count > 0
                ? System.Text.Json.JsonSerializer.Serialize(r.Denominations.Where(d => d.Count > 0).Select(d => new { value = d.Value, count = d.Count }))
                : null;
            await PosCashDrawer.SyncVarianceAsync(_db, _posting, shift, token);
            await _db.SaveChangesAsync(token);
            return Mapper.Map<PosShiftDto>(shift);
        }, ct);

    /// <summary>إيداع/صرف نقدي على درج الوردية المفتوحة مع قيده (مقابل الصندوق).</summary>
    public Task<PosCashMovementDto> AddCashMovementAsync(CashMovementRequestDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var me = Me;
            if (!Enum.IsDefined(r.Type)) throw new ValidationFailedException("نوع الحركة إيداع أو صرف.");
            if (r.Amount <= 0) throw new ValidationFailedException("المبلغ يجب أن يكون موجباً.");
            if (string.IsNullOrWhiteSpace(r.Reason)) throw new ValidationFailedException("سبب الحركة مطلوب.");
            var shift = await _db.Set<PosShift>().FirstOrDefaultAsync(s => s.CashierId == me && s.Status == PosShiftStatus.Open, token)
                ?? throw new ConflictException("افتح وردية أولاً.");
            var amount = DocumentPricing.Round(r.Amount);
            if (r.Type == PosCashMovementType.PaidOut && amount > PosCashDrawer.ExpectedCash(shift))
                throw new ConflictException($"المبلغ أكبر من النقد المتوقع في الدرج ({PosCashDrawer.ExpectedCash(shift):0.00}).");

            var counter = string.IsNullOrWhiteSpace(r.CounterAccountCode)
                ? (r.Type == PosCashMovementType.PaidOut ? DefaultAccounts.PettyCashExpenses : DefaultAccounts.DefaultBank)
                : r.CounterAccountCode.Trim();
            if (counter == DefaultAccounts.PettyCashExpenses) await DefaultAccounts.EnsureAsync(_db, token, counter);
            var cash = await PosCashDrawer.CashAccountAsync(_db, token);
            if (counter == cash) throw new ValidationFailedException("الحساب المقابل لا يكون حساب الصندوق نفسه.");

            var movement = new PosCashMovement
            {
                ShiftId = shift.Id, Type = r.Type, Amount = amount, Reason = r.Reason.Trim(), CounterAccountCode = counter,
                CreatedByName = _user.Name ?? string.Empty,
            };
            _db.Add(movement);
            await _db.SaveChangesAsync(token);

            var paidIn = r.Type == PosCashMovementType.PaidIn;
            var posted = await _posting.PostAsync(new GenericPostingRequest
            {
                Date = DateTime.UtcNow,
                Description = $"{(paidIn ? "إيداع نقدي في" : "صرف نقدي من")} درج الوردية {shift.ShiftNumber} - {movement.Reason}",
                SourceType = "pos_cash_movement", SourceId = movement.Id, SourceNumber = shift.ShiftNumber,
                Lines = paidIn
                    ? new List<PostingLine> { new(cash, amount, 0), new(counter, 0, amount) }
                    : new List<PostingLine> { new(counter, amount, 0), new(cash, 0, amount) },
            }, token);
            movement.JournalEntryId = posted.JournalEntryId;
            if (paidIn) shift.TotalCashIn += amount; else shift.TotalCashOut += amount;
            await _db.SaveChangesAsync(token);
            return Mapper.Map<PosCashMovementDto>(movement);
        }, ct);

    public async Task<List<PosCashMovementDto>> ListCashMovementsAsync(Guid shiftId, CancellationToken ct = default)
        => (await _db.Set<PosCashMovement>().AsNoTracking().Where(m => m.ShiftId == shiftId).OrderBy(m => m.CreatedAt).ToListAsync(ct))
            .Select(Mapper.Map<PosCashMovementDto>).ToList();

    /// <summary>إلغاء حركة نقدية: يُعكس قيدها وتُطرح من الوردية (الوردية المفتوحة لكاشيرها، والمغلقة للأدوار الإدارية).</summary>
    public Task DeleteCashMovementAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var m = await _db.Set<PosCashMovement>().FirstOrDefaultAsync(x => x.Id == id, token) ?? throw new NotFoundException("الحركة غير موجودة");
            var shift = await _db.Set<PosShift>().FirstAsync(s => s.Id == m.ShiftId, token);
            PosShiftRules.EnsureCanCorrect(shift, _user);
            await RemoveMovementAsync(m, shift, token);
            await PosCashDrawer.SyncVarianceAsync(_db, _posting, shift, token);
            await _db.SaveChangesAsync(token);
        }, ct);

    private async Task RemoveMovementAsync(PosCashMovement m, PosShift shift, CancellationToken ct)
    {
        if (m.JournalEntryId.HasValue) await _posting.ReverseAsync(m.JournalEntryId.Value, "إلغاء حركة نقدية على درج الكاشير", ct);
        if (m.Type == PosCashMovementType.PaidIn) shift.TotalCashIn -= m.Amount; else shift.TotalCashOut -= m.Amount;
        _db.Remove(m);
    }

    /// <summary>تقرير X (وردية مفتوحة) أو Z (مغلقة): المبيعات بالطرق، الحركات النقدية، النقد المتوقع، أعلى الأصناف.</summary>
    public async Task<PosShiftReportDto> GetReportAsync(Guid? shiftId, CancellationToken ct = default)
    {
        var me = Me;
        var shift = shiftId.HasValue
            ? await _db.Set<PosShift>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == shiftId, ct) ?? throw new NotFoundException("الوردية غير موجودة")
            : await _db.Set<PosShift>().AsNoTracking().FirstOrDefaultAsync(s => s.CashierId == me && s.Status == PosShiftStatus.Open, ct)
                ?? throw new NotFoundException("لا توجد وردية مفتوحة.");
        if (shift.CashierId != me && !PosShiftRules.IsPrivileged(_user))
            throw new ForbiddenException("تقارير ورديات الكاشيرين الآخرين للأدوار الإدارية فقط.");

        var sales = await _db.Set<PosTransaction>().AsNoTracking().Include(t => t.Items).Where(t => t.ShiftId == shift.Id).ToListAsync(ct);
        var live = sales.Where(t => t.Status != PosTransactionStatus.Voided).ToList();
        var returnsCount = await _db.Set<PosSalesReturn>().CountAsync(x => x.ShiftId == shift.Id, ct);
        var movements = await ListCashMovementsAsync(shift.Id, ct);

        return new PosShiftReportDto
        {
            ReportType = shift.Status == PosShiftStatus.Open ? "X" : "Z",
            Shift = Mapper.Map<PosShiftDto>(shift),
            GeneratedAt = DateTime.UtcNow,
            SalesCount = live.Count,
            VoidedCount = sales.Count - live.Count,
            ReturnsCount = returnsCount,
            AverageTicket = live.Count == 0 ? 0 : DocumentPricing.Round(live.Sum(t => t.GrandTotal) / live.Count),
            NetSales = DocumentPricing.Round(live.Sum(t => t.GrandTotal - t.VatAmount)),
            CashIn = shift.TotalCashIn, CashOut = shift.TotalCashOut,
            ExpectedCash = PosCashDrawer.ExpectedCash(shift),
            TopItems = live.SelectMany(t => t.Items).GroupBy(i => i.ItemId)
                .Select(g => new PosShiftReportItemDto { ItemId = g.Key, NameAr = g.First().NameAr, Quantity = g.Sum(i => i.Quantity), Total = g.Sum(i => i.TotalWithVat) })
                .OrderByDescending(i => i.Total).Take(10).ToList(),
            CashMovements = movements,
        };
    }

    public Task<PosShiftDto> UpdateAsync(Guid id, UpdatePosShiftRequestDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var shift = await _db.Set<PosShift>().FirstOrDefaultAsync(s => s.Id == id, token) ?? throw new NotFoundException("الوردية غير موجودة");
            PosShiftRules.EnsureCanCorrect(shift, _user);
            if (r.OpeningCash < 0 || r.ClosingCashActual < 0) throw new ValidationFailedException("المبالغ لا تكون سالبة.");
            if (string.IsNullOrWhiteSpace(r.PosTerminalName)) throw new ValidationFailedException("اسم الجهاز/الكاشير مطلوب.");
            var terminal = r.PosTerminalName.Trim();
            if (shift.Status == PosShiftStatus.Open && terminal != shift.PosTerminalName
                && await _db.Set<PosShift>().AnyAsync(s => s.Id != id && s.PosTerminalName == terminal && s.Status == PosShiftStatus.Open, token))
                throw new ConflictException("هذا الجهاز عليه وردية مفتوحة لكاشير آخر.");

            shift.PosTerminalName = terminal; shift.OpeningCash = r.OpeningCash; shift.ClosingNotes = r.ClosingNotes;
            if (shift.Status == PosShiftStatus.Closed && r.ClosingCashActual.HasValue) shift.ClosingCashActual = r.ClosingCashActual;
            await PosCashDrawer.SyncVarianceAsync(_db, _posting, shift, token);
            await _db.SaveChangesAsync(token);
            return Mapper.Map<PosShiftDto>(shift);
        }, ct);

    public Task DeleteAsync(Guid id, bool cascade, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var shift = await _db.Set<PosShift>().FirstOrDefaultAsync(s => s.Id == id, token) ?? throw new NotFoundException("الوردية غير موجودة");
            PosShiftRules.EnsureCanCorrect(shift, _user);
            var returnIds = await _db.Set<PosSalesReturn>().Where(x => x.ShiftId == id).Select(x => x.Id).ToListAsync(token);
            var saleIds = await _db.Set<PosTransaction>().Where(t => t.ShiftId == id).Select(t => t.Id).ToListAsync(token);
            var movements = await _db.Set<PosCashMovement>().Where(m => m.ShiftId == id).ToListAsync(token);
            if (returnIds.Count + saleIds.Count + movements.Count > 0)
            {
                if (!cascade) throw new ConflictException("للوردية معاملات أو مرتجعات أو حركات نقدية؛ استخدم cascade=true لحذفها كلها بعكس أثرها.");
                if (!PosShiftRules.IsPrivileged(_user)) throw new ForbiddenException("حذف وردية بمحتوياتها للأدوار الإدارية فقط.");
                foreach (var rid in returnIds) await _returns.DeleteAsync(rid, token);
                // مرتجعات أُنشئت على معاملات هذه الوردية من ورديات أخرى تُحذف أيضاً قبل المعاملة
                var foreign = await _db.Set<PosSalesReturn>().Where(x => saleIds.Contains(x.OriginalTransactionId)).Select(x => x.Id).ToListAsync(token);
                foreach (var rid in foreign) await _returns.DeleteAsync(rid, token);
                foreach (var sid in saleIds) await _sales.DeleteAsync(sid, token);
            }
            var fresh = await _db.Set<PosShift>().FirstAsync(s => s.Id == id, token);
            foreach (var m in movements) await RemoveMovementAsync(m, fresh, token);
            if (fresh.VarianceJournalEntryId is Guid variance)
                await _posting.ReverseAsync(variance, $"حذف الوردية {fresh.ShiftNumber}", token);
            _db.Remove(fresh);
            await _db.SaveChangesAsync(token);
        }, ct);

    public async Task<PagedResult<PosShiftDto>> ListAsync(PaginationParams p, CancellationToken ct = default)
    {
        var q = _db.Set<PosShift>().AsNoTracking().AsQueryable();
        if (Enum.TryParse<PosShiftStatus>(p.Status, true, out var st)) q = q.Where(s => s.Status == st);
        if (p.StartDate.HasValue) q = q.Where(s => s.OpenedAt >= p.StartDate);
        if (p.EndDate.HasValue) q = q.Where(s => s.OpenedAt <= p.EndDate);
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var t = p.SearchTerm.Trim();
            q = q.Where(s => s.ShiftNumber.Contains(t) || s.CashierName.Contains(t) || s.PosTerminalName.Contains(t));
        }
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(s => s.OpenedAt).Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);
        return new PagedResult<PosShiftDto>
        {
            Items = items.Select(Mapper.Map<PosShiftDto>).ToList(),
            TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    public async Task<PosShiftDto> GetAsync(Guid id, CancellationToken ct = default)
        => Mapper.Map<PosShiftDto>(await _db.Set<PosShift>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("الوردية غير موجودة"));
}
