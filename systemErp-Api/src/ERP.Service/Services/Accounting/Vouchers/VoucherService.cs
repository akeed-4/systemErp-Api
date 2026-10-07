using ERP.Core.Contracts.Shared;
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
    private readonly IAuditService _audit;

    public VoucherService(ErpDbContext db, IAccountingPostingService posting, INumberSequenceService numbers, ITransactionRunner tx, IAuditService audit)
    {
        _audit = audit;
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
        var items = await q.Include(v => v.PaymentSplits).Include(v => v.Allocations).OrderByDescending(v => v.Date).ThenByDescending(v => v.VoucherNumber)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);
        return new PagedResult<VoucherDto>
        {
            Items = items.Select(Mapper.Map<VoucherDto>).ToList(),
            TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    public async Task<VoucherDto> GetAsync(Guid id, CancellationToken ct = default)
        => Mapper.Map<VoucherDto>(await _db.Set<Voucher>().AsNoTracking().Include(v => v.PaymentSplits).Include(v => v.Allocations).FirstOrDefaultAsync(v => v.Id == id, ct)
            ?? throw new NotFoundException(Messages.VoucherNotFound));

    public Task<VoucherDto> CreateAsync(CreateVoucherDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            Validate(r);
            await PrepareAllocationsAsync(r, null, token);
            var voucher = Mapper.Map<Voucher>(r);
            voucher.Date = r.Date == default ? DateTime.UtcNow : r.Date;
            voucher.VoucherNumber = await _numbers.NextAsync(r.Type == VoucherType.Receipt ? "receipt_voucher" : "payment_voucher",
                r.Type == VoucherType.Receipt ? "RV-" : "PV-", token);
            voucher.AmountInWordsAr = ArabicAmountInWords.Riyals(r.Amount);
            _db.Add(voucher);
            await _db.SaveChangesAsync(token);
            await PostAsync(voucher, r, token);
            await _audit.LogAsync("VOUCHER_CREATED", nameof(Voucher), voucher.Id.ToString(), $"سند {voucher.VoucherNumber} بمبلغ {voucher.Amount:0.00} - {voucher.PartyName}", token);
            return await GetAsync(voucher.Id, token);
        }, ct);

    /// <summary>تعديل سند: يُعكس قيده القديم ويُرحَّل قيد جديد بنفس رقم السند (النوع لا يتغيّر).</summary>
    public Task<VoucherDto> UpdateAsync(Guid id, UpdateVoucherDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var voucher = await _db.Set<Voucher>().Include(v => v.PaymentSplits).Include(v => v.Allocations).FirstOrDefaultAsync(v => v.Id == id, token)
                ?? throw new NotFoundException(Messages.VoucherNotFound);
            if (r.Type != voucher.Type) throw new ConflictException(Messages.CannotChangeVoucherType);
            Validate(r);
            await PrepareAllocationsAsync(r, voucher.Id, token);

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
            _db.RemoveRange(voucher.Allocations);
            voucher.Allocations.Clear();
            foreach (var a in r.Allocations)
                voucher.Allocations.Add(new VoucherAllocation { InvoiceId = a.InvoiceId, InvoiceNumber = a.InvoiceNumber ?? string.Empty, Amount = a.Amount });
            await _db.SaveChangesAsync(token);

            await PostAsync(voucher, r, token);
            await _audit.LogAsync("VOUCHER_UPDATED", nameof(Voucher), id.ToString(), $"تعديل السند {voucher.VoucherNumber}: المبلغ {voucher.Amount:0.00}", token);
            return await GetAsync(id, token);
        }, ct);

    private static void Validate(CreateVoucherDto r)
    {
        var errors = new List<string>();
        if (r.Amount <= 0) errors.Add(Messages.VoucherAmountMustBePositive);
        if (string.IsNullOrWhiteSpace(r.PartyName)) errors.Add(Messages.PartyNameRequired);
        if (string.IsNullOrWhiteSpace(r.PartyAccountCode)) errors.Add(Messages.PartyAccountRequired);
        if (!r.IsSplitPayment && string.IsNullOrWhiteSpace(r.TreasuryAccountCode)) errors.Add(Messages.TreasuryAccountRequired);
        if (r.IsSplitPayment && Math.Abs(r.PaymentSplits.Sum(s => s.Amount) - r.Amount) > 0.005m)
            errors.Add(Messages.SplitPaymentsMustEqualVoucherAmount);
        if (r.VatAmount < 0 || (r.VatAmount > 0 && r.VatAmount >= r.Amount)) errors.Add(Messages.VoucherVatInvalid);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
    }

    /// <summary>
    /// يتحقق من توزيع السند على الفواتير ويملأ أرقامها: فواتير آجلة مرحّلة للطرف نفسه (قبض ← مبيعات، صرف ← مشتريات)،
    /// كل مبلغ لا يتجاوز المتبقي على فاتورته، والمجموع لا يتجاوز صافي السند. والضريبة المتضمَّنة لمصروف/إيراد مباشر لا لسداد ذمة.
    /// </summary>
    private async Task PrepareAllocationsAsync(CreateVoucherDto r, Guid? voucherId, CancellationToken ct)
    {
        var partyAccount = await _db.Set<Account>().AsNoTracking().Where(a => a.Code == r.PartyAccountCode)
            .Select(a => new { a.LinkedEntityType, a.LinkedEntityId }).FirstOrDefaultAsync(ct);
        var isPartyLedger = partyAccount?.LinkedEntityType is LinkedEntityType.Customer or LinkedEntityType.Supplier;
        if (r.VatAmount > 0 && isPartyLedger) throw new ValidationFailedException(Messages.VoucherVatNotForPartySettlement);
        if (r.Allocations.Count == 0) return;

        var errors = new List<string>();
        if (!isPartyLedger) errors.Add(Messages.AllocationNeedsPartyAccount);
        if (r.Allocations.Any(a => a.Amount <= 0)) errors.Add(Messages.AllocationAmountsMustBePositive);
        if (r.Allocations.GroupBy(a => a.InvoiceId).Any(g => g.Count() > 1)) errors.Add(Messages.AllocationInvoiceRepeated);
        if (r.Allocations.Sum(a => a.Amount) - (r.Amount - r.VatAmount) > 0.005m) errors.Add(Messages.AllocationsExceedVoucher);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        var ids = r.Allocations.Select(a => a.InvoiceId).ToList();
        var kind = r.Type == VoucherType.Receipt ? InvoiceKind.Sales : InvoiceKind.Purchase;
        var invoices = await _db.Set<Invoice>().AsNoTracking().Where(i => ids.Contains(i.Id))
            .Select(i => new { i.Id, i.InvoiceNumber, i.Kind, i.Status, i.PartyId }).ToDictionaryAsync(i => i.Id, ct);
        var balances = await InvoiceBalances.ForAsync(_db, ids, voucherId, ct);
        foreach (var a in r.Allocations)
        {
            if (!invoices.TryGetValue(a.InvoiceId, out var invoice) || invoice.Status != "posted" || invoice.Kind != kind || invoice.PartyId != partyAccount!.LinkedEntityId)
                throw new ValidationFailedException(Messages.AllocationInvoiceNotForParty);
            var due = balances.GetValueOrDefault(a.InvoiceId)?.AmountDue ?? 0;
            if (a.Amount - due > 0.005m) throw new ValidationFailedException(string.Format(Messages.AllocationExceedsInvoiceDue, invoice.InvoiceNumber, due));
            a.InvoiceNumber = invoice.InvoiceNumber;
        }
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
            PartyAccountCode = r.PartyAccountCode, Treasury = treasury, VatAmount = r.VatAmount,
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
            var voucher = await _db.Set<Voucher>().Include(v => v.PaymentSplits).Include(v => v.Allocations).FirstOrDefaultAsync(v => v.Id == id, token)
                ?? throw new NotFoundException(Messages.VoucherNotFound);
            // السند المرحَّل لا يُحذف فيزيائياً: يُعكس قيده ثم يُزال السند (مطابقاً لسلوك deleteVoucher في الواجهة).
            if (voucher.JournalEntryId.HasValue) await _posting.ReverseAsync(voucher.JournalEntryId.Value, $"حذف السند {voucher.VoucherNumber}", token);
            _db.RemoveRange(voucher.PaymentSplits);
            _db.RemoveRange(voucher.Allocations); // الفواتير الموزَّع عليها تعود بمتبقّيها
            _db.Remove(voucher);
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("VOUCHER_DELETED", nameof(Voucher), id.ToString(), $"حذف السند {voucher.VoucherNumber} بمبلغ {voucher.Amount:0.00} وعكس قيده", token);
        }, ct);
}
