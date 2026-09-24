using ERP.Core.Contracts.POS;
using ERP.Core.DTOs.POS;
using ERP.Service.Data;
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

    public PosShiftService(ErpDbContext db, ICurrentUser user, INumberSequenceService numbers, ITransactionRunner tx,
        IPosSaleService sales, IPosSaleReturnService returns)
    {
        _db = db; _user = user; _numbers = numbers; _tx = tx; _sales = sales; _returns = returns;
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

    public async Task<PosShiftDto> CloseAsync(CloseShiftRequestDto r, CancellationToken ct = default)
    {
        var me = Me;
        if (r.ClosingCashActual < 0) throw new ValidationFailedException("النقد الفعلي لا يكون سالباً.");
        var shift = await _db.Set<PosShift>().FirstOrDefaultAsync(s => s.CashierId == me && s.Status == PosShiftStatus.Open, ct)
            ?? throw new NotFoundException("لا توجد وردية مفتوحة لإغلاقها.");

        shift.ClosedAt = DateTime.UtcNow; shift.Status = PosShiftStatus.Closed;
        shift.ClosingCashActual = r.ClosingCashActual;
        shift.CashVariance = r.ClosingCashActual - (shift.OpeningCash + shift.TotalCashSales - shift.TotalCashRefunds);
        shift.ClosingNotes = r.ClosingNotes;
        await _db.SaveChangesAsync(ct);
        return Mapper.Map<PosShiftDto>(shift);
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
            PosShiftRules.RecomputeVariance(shift);
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
            if (returnIds.Count + saleIds.Count > 0)
            {
                if (!cascade) throw new ConflictException("للوردية معاملات ومرتجعات؛ استخدم cascade=true لحذفها كلها بعكس أثرها.");
                if (!PosShiftRules.IsPrivileged(_user)) throw new ForbiddenException("حذف وردية بمحتوياتها للأدوار الإدارية فقط.");
                foreach (var rid in returnIds) await _returns.DeleteAsync(rid, token);
                // مرتجعات أُنشئت على معاملات هذه الوردية من ورديات أخرى تُحذف أيضاً قبل المعاملة
                var foreign = await _db.Set<PosSalesReturn>().Where(x => saleIds.Contains(x.OriginalTransactionId)).Select(x => x.Id).ToListAsync(token);
                foreach (var rid in foreign) await _returns.DeleteAsync(rid, token);
                foreach (var sid in saleIds) await _sales.DeleteAsync(sid, token);
            }
            var fresh = await _db.Set<PosShift>().FirstAsync(s => s.Id == id, token);
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
