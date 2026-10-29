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
    private readonly IAccountingPostingService _posting;

    public PosShiftService(ErpDbContext db, ICurrentUser user, INumberSequenceService numbers, ITransactionRunner tx,
        IAccountingPostingService posting)
    {
        _db = db; _user = user; _numbers = numbers; _tx = tx; _posting = posting;
    }

    private Guid Me => _user.UserId ?? throw new UnauthorizedAppException();

    public async Task<PosShiftDto?> GetActiveAsync(CancellationToken ct = default)
    {
        var me = Me;
        var shift = await _db.Set<PosShift>().AsNoTracking().FirstOrDefaultAsync(s => s.CashierId == me && s.Status == PosShiftStatus.Open, ct);
        return shift == null ? null : ToDto(shift);
    }

    public Task<PosShiftDto> OpenAsync(OpenShiftRequestDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var me = Me;
            if (r.OpeningCash < 0) throw new ValidationFailedException(Messages.OpeningCashCannotBeNegative);
            PosTerminal? terminal = null;
            if (r.PosTerminalId.HasValue)
            {
                terminal = await _db.Set<PosTerminal>().FirstOrDefaultAsync(t => t.Id == r.PosTerminalId && t.IsActive, token)
                    ?? throw new ValidationFailedException("نقطة البيع غير موجودة أو غير مفعلة.");
                var allowedUsers = PosTerminalService.ReadIds(terminal.AllowedUserIdsJson);
                if (allowedUsers.Count > 0 && !allowedUsers.Contains(me)) throw new ForbiddenException("هذا المستخدم غير مخول للعمل على نقطة البيع المحددة.");
                if (await _db.Set<PosShift>().AnyAsync(s => s.PosTerminalId == terminal.Id && s.Status == PosShiftStatus.Open, token))
                    throw new ConflictException("توجد وردية مفتوحة على نقطة البيع المحددة.");
            }
            var terminalName = terminal?.Name ?? r.PosTerminalName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(terminalName)) throw new ValidationFailedException(Messages.TerminalNameRequired);
            if (await _db.Set<PosShift>().AnyAsync(s => s.CashierId == me && s.Status == PosShiftStatus.Open, token))
                throw new ConflictException(Messages.ShiftAlreadyOpen);
            if (terminal == null && await _db.Set<PosShift>().AnyAsync(s => s.PosTerminalId == null && s.PosTerminalName == terminalName && s.Status == PosShiftStatus.Open, token))
                throw new ConflictException(Messages.TerminalHasOpenShift);

            var shift = new PosShift
            {
                ShiftNumber = await _numbers.NextAsync("pos_shift", "SHF-", token),
                CashierId = me, CashierName = _user.Name ?? string.Empty, PosTerminalName = terminalName,
                PosTerminalId = terminal?.Id, WarehouseId = terminal?.WarehouseId,
                AllowReturns = terminal?.AllowReturns ?? true, AllowDiscount = terminal?.AllowDiscount ?? true,
                MaxDiscountPercent = terminal?.MaxDiscountPercent,
                AllowedCategoryIdsJson = terminal?.AllowedCategoryIdsJson ?? "[]",
                OpenedAt = DateTime.UtcNow, OpeningCash = r.OpeningCash, Status = PosShiftStatus.Open,
            };
            _db.Add(shift);
            await _db.SaveChangesAsync(token);
            return ToDto(shift);
        }, ct);

    /// <summary>إغلاق الوردية: الجرد بالفئات (إن أُرسل) يحدد النقد الفعلي، ويُرحَّل قيد العجز/الزيادة.</summary>
    public Task<PosShiftDto> CloseAsync(CloseShiftRequestDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var me = Me;
            var actual = r.ClosingCashActual;
            if (r.Denominations.Count > 0)
            {
                if (r.Denominations.Any(d => d.Value <= 0 || d.Count < 0)) throw new ValidationFailedException(Messages.DenominationsInvalid);
                actual = DocumentPricing.Round(r.Denominations.Sum(d => d.Value * d.Count));
                if (r.ClosingCashActual != 0 && r.ClosingCashActual != actual)
                    throw new ValidationFailedException(string.Format(Messages.DenominationsMismatchCash, actual, r.ClosingCashActual));
            }
            if (actual < 0) throw new ValidationFailedException(Messages.ActualCashCannotBeNegative);
            var shift = await _db.Set<PosShift>().FirstOrDefaultAsync(s => s.CashierId == me && s.Status == PosShiftStatus.Open, token)
                ?? throw new NotFoundException(Messages.NoOpenShiftToClose);
            var unresolved = await CardTerminalPaymentService.Unresolved(_db.Set<CardTerminalPayment>().Where(p => p.ShiftId == shift.Id)).CountAsync(token);
            if (unresolved > 0) throw new ConflictException(string.Format(Messages.ShiftHasUnresolvedTerminalPayments, unresolved));

            shift.ClosedAt = DateTime.UtcNow; shift.Status = PosShiftStatus.Closed;
            shift.ClosingCashActual = actual;
            shift.ClosingNotes = r.ClosingNotes;
            shift.ClosingDenominations = r.Denominations.Count > 0
                ? System.Text.Json.JsonSerializer.Serialize(r.Denominations.Where(d => d.Count > 0).Select(d => new { value = d.Value, count = d.Count }))
                : null;
            await PosCashDrawer.SyncVarianceAsync(_db, _posting, shift, token);
            await _db.SaveChangesAsync(token);
            return ToDto(shift);
        }, ct);

    /// <summary>إيداع/صرف نقدي على درج الوردية المفتوحة مع قيده (مقابل الصندوق).</summary>
    public Task<PosCashMovementDto> AddCashMovementAsync(CashMovementRequestDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var me = Me;
            if (!Enum.IsDefined(r.Type)) throw new ValidationFailedException(Messages.MovementTypeValues);
            if (r.Amount <= 0) throw new ValidationFailedException(Messages.AmountMustBePositive);
            if (string.IsNullOrWhiteSpace(r.Reason)) throw new ValidationFailedException(Messages.MovementReasonRequired);
            var shift = await _db.Set<PosShift>().FirstOrDefaultAsync(s => s.CashierId == me && s.Status == PosShiftStatus.Open, token)
                ?? throw new ConflictException(Messages.OpenShiftFirst);
            var amount = DocumentPricing.Round(r.Amount);
            if (r.Type == PosCashMovementType.PaidOut && amount > PosCashDrawer.ExpectedCash(shift))
                throw new ConflictException(string.Format(Messages.AmountExceedsExpectedCash, PosCashDrawer.ExpectedCash(shift)));

            var counter = string.IsNullOrWhiteSpace(r.CounterAccountCode)
                ? (r.Type == PosCashMovementType.PaidOut ? DefaultAccounts.PettyCashExpenses : DefaultAccounts.DefaultBank)
                : r.CounterAccountCode.Trim();
            if (counter == DefaultAccounts.PettyCashExpenses) await DefaultAccounts.EnsureAsync(_db, token, counter);
            var cash = string.IsNullOrWhiteSpace(r.CashAccountCode) ? await PosCashDrawer.CashAccountAsync(_db, token) : r.CashAccountCode.Trim();
            if (counter == cash) throw new ValidationFailedException(Messages.CounterAccountCannotBeCash);

            var movement = new PosCashMovement
            {
                ShiftId = shift.Id, Type = r.Type, Amount = amount, Reason = r.Reason.Trim(), CounterAccountCode = counter, CashAccountCode = cash,
                CreatedByName = _user.Name ?? string.Empty,
            };
            _db.Add(movement);
            await _db.SaveChangesAsync(token);

            var paidIn = r.Type == PosCashMovementType.PaidIn;
            var posted = await _posting.PostAsync(new GenericPostingRequest
            {
                Date = SaudiTime.Today,
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

    public async Task<PosCashAccountsDto> GetCashAccountsAsync(CancellationToken ct = default)
    {
        // الحسابات الفرعية فقط: الحساب الأب لا يُرحَّل عليه
        var accounts = await _db.Set<Account>().AsNoTracking()
            .Where(a => !_db.Set<Account>().Any(c => c.ParentCode == a.Code))
            .OrderBy(a => a.Code)
            .Select(a => new PosAccountOptionDto { Code = a.Code, NameAr = a.NameAr, NameEn = a.NameEn })
            .ToListAsync(ct);
        return new PosCashAccountsDto { DefaultCashAccountCode = await PosCashDrawer.CashAccountAsync(_db, ct), Accounts = accounts };
    }

    public async Task<List<PosCashMovementDto>> ListCashMovementsAsync(Guid shiftId, CancellationToken ct = default)
        => (await _db.Set<PosCashMovement>().AsNoTracking().Where(m => m.ShiftId == shiftId).OrderBy(m => m.CreatedAt).ToListAsync(ct))
            .Select(Mapper.Map<PosCashMovementDto>).ToList();

    /// <summary>إلغاء حركة نقدية: يُعكس قيدها وتُطرح من الوردية (الوردية المفتوحة لكاشيرها، والمغلقة للأدوار الإدارية).</summary>
    public Task DeleteCashMovementAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var m = await _db.Set<PosCashMovement>().FirstOrDefaultAsync(x => x.Id == id, token) ?? throw new NotFoundException(Messages.MovementNotFound);
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
            ? await _db.Set<PosShift>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == shiftId, ct) ?? throw new NotFoundException(Messages.ShiftNotFound)
            : await _db.Set<PosShift>().AsNoTracking().FirstOrDefaultAsync(s => s.CashierId == me && s.Status == PosShiftStatus.Open, ct)
                ?? throw new NotFoundException(Messages.NoOpenShift);
        if (shift.CashierId != me && !PosShiftRules.IsPrivileged(_user))
            throw new ForbiddenException(Messages.OtherCashierShiftReportsAdminOnly);

        var sales = await _db.Set<PosTransaction>().AsNoTracking().Include(t => t.Items).Where(t => t.ShiftId == shift.Id).ToListAsync(ct);
        var live = sales.Where(t => t.Status != PosTransactionStatus.Voided).ToList();
        var returnsCount = await _db.Set<PosSalesReturn>().CountAsync(x => x.ShiftId == shift.Id, ct);
        var movements = await ListCashMovementsAsync(shift.Id, ct);

        return new PosShiftReportDto
        {
            ReportType = shift.Status == PosShiftStatus.Open ? "X" : "Z",
            Shift = ToDto(shift),
            GeneratedAt = DateTime.UtcNow,
            SalesCount = live.Count,
            VoidedCount = sales.Count - live.Count,
            ReturnsCount = returnsCount,
            AverageTicket = live.Count == 0 ? 0 : DocumentPricing.Round(live.Sum(t => t.GrandTotal) / live.Count),
            NetSales = DocumentPricing.Round(live.Sum(t => t.GrandTotal - t.VatAmount - t.ExciseTaxAmount)),
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
            var shift = await _db.Set<PosShift>().FirstOrDefaultAsync(s => s.Id == id, token) ?? throw new NotFoundException(Messages.ShiftNotFound);
            PosShiftRules.EnsureCanCorrect(shift, _user);
            if (r.OpeningCash < 0 || r.ClosingCashActual < 0) throw new ValidationFailedException(Messages.AmountsCannotBeNegative);
            if (string.IsNullOrWhiteSpace(r.PosTerminalName)) throw new ValidationFailedException(Messages.TerminalNameRequired);
            var terminal = r.PosTerminalName.Trim();
            if (shift.Status == PosShiftStatus.Open && terminal != shift.PosTerminalName
                && await _db.Set<PosShift>().AnyAsync(s => s.Id != id && s.PosTerminalName == terminal && s.Status == PosShiftStatus.Open, token))
                throw new ConflictException(Messages.TerminalHasOpenShift);

            shift.PosTerminalName = terminal; shift.OpeningCash = r.OpeningCash; shift.ClosingNotes = r.ClosingNotes;
            if (shift.Status == PosShiftStatus.Closed && r.ClosingCashActual.HasValue) shift.ClosingCashActual = r.ClosingCashActual;
            await PosCashDrawer.SyncVarianceAsync(_db, _posting, shift, token);
            await _db.SaveChangesAsync(token);
            return ToDto(shift);
        }, ct);

    public Task DeleteAsync(Guid id, bool cascade, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var shift = await _db.Set<PosShift>().FirstOrDefaultAsync(s => s.Id == id, token) ?? throw new NotFoundException(Messages.ShiftNotFound);
            PosShiftRules.EnsureCanCorrect(shift, _user);
            var returnIds = await _db.Set<PosSalesReturn>().Where(x => x.ShiftId == id).Select(x => x.Id).ToListAsync(token);
            var saleIds = await _db.Set<PosTransaction>().Where(t => t.ShiftId == id).Select(t => t.Id).ToListAsync(token);
            var movements = await _db.Set<PosCashMovement>().Where(m => m.ShiftId == id).ToListAsync(token);
            // وردية صدرت فيها فواتير أو مرتجعات لا تُحذف: مستنداتها الضريبية باقية ومرتبطة بها
            if (returnIds.Count + saleIds.Count > 0) throw new ConflictException(Messages.ShiftWithSalesCannotBeDeleted);
            if (movements.Count > 0)
            {
                if (!cascade) throw new ConflictException(Messages.ShiftHasContentUseCascade);
                if (!PosShiftRules.IsPrivileged(_user)) throw new ForbiddenException(Messages.DeleteShiftWithContentAdminOnly);
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
            Items = items.Select(ToDto).ToList(),
            TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    public async Task<PosShiftDto> GetAsync(Guid id, CancellationToken ct = default)
        => ToDto(await _db.Set<PosShift>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException(Messages.ShiftNotFound));

    private static PosShiftDto ToDto(PosShift shift)
    {
        var dto = Mapper.Map<PosShiftDto>(shift);
        dto.AllowedCategoryIds = PosTerminalService.ReadIds(shift.AllowedCategoryIdsJson);
        return dto;
    }
}
