using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using ERP.Service.Services.Shared.Text;

namespace ERP.Service.Services.Accounting;

public class VoucherService : IVoucherService
{
    private readonly ErpDbContext _db;
    private readonly IAccountingPostingService _posting;
    private readonly INumberSequenceService _numbers;
    private readonly ITransactionRunner _tx;

    public VoucherService(ErpDbContext db, IAccountingPostingService posting, INumberSequenceService numbers, ITransactionRunner tx)
    {
        _db = db; _posting = posting; _numbers = numbers; _tx = tx;
    }

    public async Task<PagedResult<VoucherDto>> ListAsync(PaginationParams p, CancellationToken ct = default)
    {
        var q = _db.Set<Voucher>().AsNoTracking().AsQueryable();
        if (p.StartDate.HasValue) q = q.Where(v => v.Date >= p.StartDate);
        if (p.EndDate.HasValue) q = q.Where(v => v.Date <= p.EndDate);
        if (!string.IsNullOrWhiteSpace(p.Status) && Enum.TryParse<VoucherType>(p.Status, true, out var type)) q = q.Where(v => v.Type == type);
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var t = p.SearchTerm.Trim();
            q = q.Where(v => v.VoucherNumber.Contains(t) || v.PartyName.Contains(t) || (v.ReferenceNumber != null && v.ReferenceNumber.Contains(t)));
        }
        var total = await q.CountAsync(ct);
        var items = await q.Include(v => v.PaymentSplits).OrderByDescending(v => v.Date).ThenByDescending(v => v.VoucherNumber)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);
        return new PagedResult<VoucherDto>
        {
            Items = items.Select(Mapper.Map<VoucherDto>).ToList(),
            TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    public async Task<VoucherDto> GetAsync(Guid id, CancellationToken ct = default)
        => Mapper.Map<VoucherDto>(await _db.Set<Voucher>().AsNoTracking().Include(v => v.PaymentSplits).FirstOrDefaultAsync(v => v.Id == id, ct)
            ?? throw new NotFoundException("السند غير موجود"));

    public Task<VoucherDto> CreateAsync(CreateVoucherDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            Validate(r);
            var voucher = Mapper.Map<Voucher>(r);
            voucher.Date = r.Date == default ? DateTime.UtcNow : r.Date;
            voucher.VoucherNumber = await _numbers.NextAsync(r.Type == VoucherType.Receipt ? "receipt_voucher" : "payment_voucher",
                r.Type == VoucherType.Receipt ? "RV-" : "PV-", token);
            voucher.AmountInWordsAr = ArabicAmountInWords.Riyals(r.Amount);
            _db.Add(voucher);
            await _db.SaveChangesAsync(token);
            await PostAsync(voucher, r, token);
            return await GetAsync(voucher.Id, token);
        }, ct);

    /// <summary>تعديل سند: يُعكس قيده القديم ويُرحَّل قيد جديد بنفس رقم السند (النوع لا يتغيّر).</summary>
    public Task<VoucherDto> UpdateAsync(Guid id, UpdateVoucherDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var voucher = await _db.Set<Voucher>().Include(v => v.PaymentSplits).FirstOrDefaultAsync(v => v.Id == id, token)
                ?? throw new NotFoundException("السند غير موجود");
            if (r.Type != voucher.Type) throw new ConflictException("لا يمكن تغيير نوع السند (قبض/صرف).");
            Validate(r);

            if (voucher.JournalEntryId.HasValue)
                await _posting.ReverseAsync(voucher.JournalEntryId.Value, $"تعديل السند {voucher.VoucherNumber}", token);

            var number = voucher.VoucherNumber;
            var date = voucher.Date;
            Mapper.Apply(r, voucher);
            voucher.VoucherNumber = number;
            voucher.Date = r.Date == default ? date : r.Date;
            voucher.AmountInWordsAr = ArabicAmountInWords.Riyals(r.Amount);
            _db.RemoveRange(voucher.PaymentSplits);
            voucher.PaymentSplits.Clear();
            foreach (var s in r.PaymentSplits)
                voucher.PaymentSplits.Add(new VoucherPaymentSplit { Method = s.Method, Amount = s.Amount, Reference = s.Reference });
            await _db.SaveChangesAsync(token);

            await PostAsync(voucher, r, token);
            return await GetAsync(id, token);
        }, ct);

    private static void Validate(CreateVoucherDto r)
    {
        var errors = new List<string>();
        if (r.Amount <= 0) errors.Add("مبلغ السند يجب أن يكون أكبر من صفر.");
        if (string.IsNullOrWhiteSpace(r.PartyName)) errors.Add("اسم الطرف مطلوب.");
        if (string.IsNullOrWhiteSpace(r.PartyAccountCode)) errors.Add("حساب الطرف مطلوب.");
        if (!r.IsSplitPayment && string.IsNullOrWhiteSpace(r.TreasuryAccountCode)) errors.Add("حساب الخزينة/البنك مطلوب.");
        if (r.IsSplitPayment && Math.Abs(r.PaymentSplits.Sum(s => s.Amount) - r.Amount) > 0.005m)
            errors.Add("مجموع الدفعات المقسّمة يجب أن يساوي مبلغ السند.");
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
    }

    private async Task PostAsync(Voucher voucher, CreateVoucherDto r, CancellationToken token)
    {
        var treasury = r.IsSplitPayment && r.PaymentSplits.Count > 0
            ? await SplitsToTreasuryAsync(r.PaymentSplits, token)
            : new List<PaymentPosting> { new(r.TreasuryAccountCode, r.Amount) };

        var posted = await _posting.PostVoucherAsync(new VoucherPostingRequest
        {
            Date = voucher.Date,
            Description = $"{(voucher.Type == VoucherType.Receipt ? "سند قبض" : "سند صرف")} {voucher.VoucherNumber} - {voucher.PartyName}",
            VoucherId = voucher.Id, VoucherNumber = voucher.VoucherNumber, Type = voucher.Type,
            PartyAccountCode = r.PartyAccountCode, Treasury = treasury,
        }, token);

        voucher.JournalEntryId = posted.JournalEntryId;
        await _db.SaveChangesAsync(token);
    }

    private async Task<List<PaymentPosting>> SplitsToTreasuryAsync(List<VoucherPaymentSplitDto> splits, CancellationToken ct)
    {
        var methods = await _db.Set<PaymentMethodItem>().AsNoTracking().ToListAsync(ct);
        return splits.Select(s => new PaymentPosting(TreasuryResolver.Resolve(s.Method, methods), s.Amount)).ToList();
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var voucher = await _db.Set<Voucher>().Include(v => v.PaymentSplits).FirstOrDefaultAsync(v => v.Id == id, token)
                ?? throw new NotFoundException("السند غير موجود");
            // السند المرحَّل لا يُحذف فيزيائياً: يُعكس قيده ثم يُزال السند (مطابقاً لسلوك deleteVoucher في الواجهة).
            if (voucher.JournalEntryId.HasValue) await _posting.ReverseAsync(voucher.JournalEntryId.Value, $"حذف السند {voucher.VoucherNumber}", token);
            _db.RemoveRange(voucher.PaymentSplits);
            _db.Remove(voucher);
            await _db.SaveChangesAsync(token);
        }, ct);
}
