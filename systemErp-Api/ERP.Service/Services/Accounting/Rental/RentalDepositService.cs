using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using DSt = ERP.Core.Models.Accounting.RentalDepositStatuses;
using SK = ERP.Core.Models.Accounting.RentalDepositSettlementKinds;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// تأمينات عقود التأجير (R3b) عبر <see cref="IVoucherService"/> القائمة، بلا قيود خاصة. القواعد:
/// <list type="bullet">
/// <item>القبض لعقد مسودة أو ساري: سند قبض نظامي حسابه المقابل دور «تأمينات عملاء التأجير» (مدين الخزينة، دائن التأمينات) بلا ضريبة.</item>
/// <item>الخصم من فاتورة فوترة للعقد نفسه (مرحّلة وعليها متبقٍّ): سند قبض للعميل موزّع على الفاتورة وخزينته حساب التأمينات (مدين
/// التأمينات، دائن العميل)، فينقص المتبقي على الفاتورة بالآلية نفسها لأي سداد. لا يتجاوز الرصيد ولا المتبقي.</item>
/// <item>الرد: سند صرف من حساب التأمينات (مدين التأمينات، دائن الخزينة)، لا يتجاوز الرصيد.</item>
/// <item>التسوية تُلغى بسبب بحذف سندها (يُعكس قيده) فيعود الرصيد؛ والتأمين يُلغى بلا تسويات قائمة. كل ذلك لعقد ساري (المغلق نهائي).</item>
/// <item>يُشترط استلام التأمين المطلوب قبل أول تسليم إن فُعّل الإعداد، ولا يُغلق العقد وعليه تأمين برصيد.</item>
/// </list>
/// </summary>
public class RentalDepositService : IRentalDepositService
{
    private readonly ErpDbContext _db;
    private readonly IVoucherService _vouchers;
    private readonly ITransactionRunner _tx;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;

    public RentalDepositService(ErpDbContext db, IVoucherService vouchers, ITransactionRunner tx, ICurrentUser user, IAuditService audit)
    {
        _db = db; _vouchers = vouchers; _tx = tx; _user = user; _audit = audit;
    }

    private string? By => _user.Name ?? _user.Email;

    public async Task<List<RentalDepositDto>> ListAsync(Guid? contractId, string? status, CancellationToken ct = default)
    {
        var q = _db.Set<RentalDeposit>().AsNoTracking().Include(d => d.Settlements).AsQueryable();
        if (contractId is Guid cid) q = q.Where(d => d.ContractId == cid);
        if (!string.IsNullOrWhiteSpace(status)) { var s = status.Trim().ToLowerInvariant(); q = q.Where(d => d.Status == s); }
        return await DtosAsync(await q.OrderByDescending(d => d.ReceivedAt).ThenByDescending(d => d.CreatedAt).Take(1000).ToListAsync(ct), ct);
    }

    public async Task<RentalDepositDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var d = await _db.Set<RentalDeposit>().AsNoTracking().Include(x => x.Settlements).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(Messages.RentalDepositNotFound);
        return (await DtosAsync(new List<RentalDeposit> { d }, ct)).Single();
    }

    public Task<RentalDepositDto> ReceiveAsync(ReceiveRentalDepositDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var c = await ContractAsync(r.ContractId, token);
            if (c.Status is not (RentalContractStatuses.Draft or RentalContractStatuses.Active)) throw new ConflictException(Messages.RentalDepositContractState);
            var amount = decimal.Round(r.Amount, 2);
            if (amount <= 0) throw new ValidationFailedException(Messages.RentalDepositAmountInvalid);
            var customer = await CustomerAsync(c.CustomerId, token);
            var liability = await LiabilityAccountAsync(token);
            var date = r.Date?.Date ?? SaudiTime.Today;

            var voucher = await _vouchers.CreateAsync(new CreateVoucherDto
            {
                SystemGenerated = true, Module = PlatformModules.EquipmentRental, Type = VoucherType.Receipt, Date = date, Amount = amount,
                PartyName = customer.NameAr, PartyAccountCode = liability, PaymentMethodCode = Trim(r.PaymentMethodCode, 50),
                TreasuryAccountCode = Trim(r.TreasuryAccountCode, 32) ?? string.Empty, ReferenceNumber = Trim(r.ReferenceNumber, 100) ?? c.ContractNumber,
                Notes = $"تأمين عقد التأجير {c.ContractNumber}{(string.IsNullOrWhiteSpace(r.Notes) ? string.Empty : " • " + r.Notes.Trim())}",
            }, token);

            var d = new RentalDeposit
            {
                ContractId = c.Id, CustomerId = c.CustomerId, Amount = amount, ReceivedAt = date, ReceiptVoucherId = voucher.Id,
                ReceiptVoucherNumber = voucher.VoucherNumber, Status = DSt.Held, Notes = Trim(r.Notes, 500), CreatedBy = By,
            };
            _db.Add(d);
            await _db.SaveChangesAsync(token);
            await LogAsync("RENTAL_DEPOSIT_RECEIVED", d, c.ContractNumber, $"{amount:0.00} • {voucher.VoucherNumber}", token);
            return await GetAsync(d.Id, token);
        }, ct);

    public Task<RentalDepositDto> ApplyAsync(Guid id, ApplyRentalDepositDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var (d, c) = await HeldAsync(id, token);
            var billing = await _db.Set<RentalBilling>().AsNoTracking()
                .FirstOrDefaultAsync(b => b.ContractId == c.Id && b.InvoiceId == r.InvoiceId && b.Status == RentalBillingStatuses.Invoiced, token)
                ?? throw new ValidationFailedException(Messages.RentalDepositInvoiceNotForContract);
            var due = (await InvoiceBalances.ForAsync(_db, new[] { r.InvoiceId }, null, token)).GetValueOrDefault(r.InvoiceId)?.AmountDue ?? 0;
            var balance = Balance(d);
            var amount = decimal.Round(r.Amount ?? Math.Min(balance, due), 2);
            if (amount <= 0) throw new ValidationFailedException(Messages.RentalDepositAmountInvalid);
            if (amount > balance) throw new ValidationFailedException(string.Format(Messages.RentalDepositExceedsBalance, balance));
            var customer = await CustomerAsync(d.CustomerId, token);
            var liability = await LiabilityAccountAsync(token);
            var date = r.Date?.Date ?? SaudiTime.Today;

            // المتبقي على الفاتورة وتطابق العميل يفحصهما توزيع السند نفسه
            var voucher = await _vouchers.CreateAsync(new CreateVoucherDto
            {
                SystemGenerated = true, Module = PlatformModules.EquipmentRental, Type = VoucherType.Receipt, Date = date, Amount = amount,
                PartyName = customer.NameAr, PartyAccountCode = customer.AccountCode, TreasuryAccountCode = liability, PaymentMethod = "deposit",
                ReferenceNumber = c.ContractNumber, Notes = $"خصم تأمين عقد التأجير {c.ContractNumber} من الفاتورة {billing.InvoiceNumber}",
                Allocations = new List<VoucherAllocationDto> { new() { InvoiceId = r.InvoiceId, Amount = amount } },
            }, token);

            return await SettleAsync(d, c, new RentalDepositSettlement
            {
                Kind = SK.Apply, Amount = amount, SettledAt = date, InvoiceId = r.InvoiceId, InvoiceNumber = billing.InvoiceNumber,
                VoucherId = voucher.Id, VoucherNumber = voucher.VoucherNumber, CreatedBy = By,
            }, "RENTAL_DEPOSIT_APPLIED", token);
        }, ct);

    public Task<RentalDepositDto> RefundAsync(Guid id, RefundRentalDepositDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var (d, c) = await HeldAsync(id, token);
            var balance = Balance(d);
            var amount = decimal.Round(r.Amount ?? balance, 2);
            if (amount <= 0) throw new ValidationFailedException(Messages.RentalDepositAmountInvalid);
            if (amount > balance) throw new ValidationFailedException(string.Format(Messages.RentalDepositExceedsBalance, balance));
            var customer = await CustomerAsync(d.CustomerId, token);
            var liability = await LiabilityAccountAsync(token);
            var date = r.Date?.Date ?? SaudiTime.Today;

            var voucher = await _vouchers.CreateAsync(new CreateVoucherDto
            {
                SystemGenerated = true, Module = PlatformModules.EquipmentRental, Type = VoucherType.Payment, Date = date, Amount = amount,
                PartyName = customer.NameAr, PartyAccountCode = liability, PaymentMethodCode = Trim(r.PaymentMethodCode, 50),
                TreasuryAccountCode = Trim(r.TreasuryAccountCode, 32) ?? string.Empty, ReferenceNumber = c.ContractNumber,
                Notes = $"رد تأمين عقد التأجير {c.ContractNumber}{(string.IsNullOrWhiteSpace(r.Reason) ? string.Empty : " • " + r.Reason.Trim())}",
            }, token);

            return await SettleAsync(d, c, new RentalDepositSettlement
            {
                Kind = SK.Refund, Amount = amount, SettledAt = date, VoucherId = voucher.Id, VoucherNumber = voucher.VoucherNumber,
                Reason = Trim(r.Reason, 500), CreatedBy = By,
            }, "RENTAL_DEPOSIT_REFUNDED", token);
        }, ct);

    public Task<RentalDepositDto> CancelSettlementAsync(Guid id, Guid settlementId, RentalReasonDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var reason = RequireReason(r);
            var (d, c) = await LoadAsync(id, token);
            if (c.Status != RentalContractStatuses.Active) throw new ConflictException(Messages.RentalDepositContractState);
            var s = d.Settlements.FirstOrDefault(x => x.Id == settlementId) ?? throw new NotFoundException(Messages.RentalDepositSettlementNotFound);
            // فك الربط أولاً ثم حذف السند بخدمته (يعكس قيده ويزيل توزيعه على الفاتورة)
            var voucherId = s.VoucherId;
            _db.Remove(s);
            d.Settlements.Remove(s);
            d.Status = DSt.Held;
            await _db.SaveChangesAsync(token);
            if (voucherId is Guid v) await _vouchers.DeleteAsync(v, token);
            await LogAsync("RENTAL_DEPOSIT_SETTLEMENT_CANCELLED", d, c.ContractNumber, $"{s.Kind} {s.Amount:0.00} • {s.VoucherNumber} • {reason}", token);
            return await GetAsync(d.Id, token);
        }, ct);

    public Task<RentalDepositDto> CancelAsync(Guid id, RentalReasonDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var reason = RequireReason(r);
            var (d, c) = await LoadAsync(id, token);
            if (d.Status == DSt.Cancelled) throw new ConflictException(Messages.RentalDepositNotHeld);
            if (c.Status is not (RentalContractStatuses.Draft or RentalContractStatuses.Active or RentalContractStatuses.Cancelled))
                throw new ConflictException(Messages.RentalDepositContractState);
            if (d.Settlements.Count > 0) throw new ConflictException(Messages.RentalDepositHasSettlements);
            var voucherId = d.ReceiptVoucherId;
            d.Status = DSt.Cancelled; d.CancelReason = reason; d.ReceiptVoucherId = null;
            await _db.SaveChangesAsync(token);
            if (voucherId is Guid v) await _vouchers.DeleteAsync(v, token);
            await LogAsync("RENTAL_DEPOSIT_CANCELLED", d, c.ContractNumber, $"{d.Amount:0.00} • {d.ReceiptVoucherNumber} • {reason}", token);
            return await GetAsync(d.Id, token);
        }, ct);

    // ---------- قواعد يستعملها التسليم والإغلاق ----------

    /// <summary>المقبوض القائم على العقد (غير الملغى) قبل التسويات: يُقارن بالتأمين المطلوب قبل التسليم.</summary>
    internal static Task<decimal> ReceivedAsync(ErpDbContext db, Guid contractId, CancellationToken ct)
        => db.Set<RentalDeposit>().Where(d => d.ContractId == contractId && d.Status != DSt.Cancelled).SumAsync(d => (decimal?)d.Amount, ct).ContinueWith(t => t.Result ?? 0m, ct);

    /// <summary>هل على العقد تأمين برصيد لم يُسوَّ (يمنع الإغلاق)؟</summary>
    internal static Task<bool> HasOpenAsync(ErpDbContext db, Guid contractId, CancellationToken ct)
        => db.Set<RentalDeposit>().AnyAsync(d => d.ContractId == contractId && d.Status == DSt.Held, ct);

    // ---------- مساعدات ----------

    private async Task<RentalDepositDto> SettleAsync(RentalDeposit d, RentalContract c, RentalDepositSettlement s, string action, CancellationToken ct)
    {
        s.DepositId = d.Id;
        _db.Add(s);
        d.Settlements.Add(s);
        if (Balance(d) <= 0) d.Status = DSt.Settled;
        await _db.SaveChangesAsync(ct);
        await LogAsync(action, d, c.ContractNumber, $"{s.Amount:0.00} • {s.VoucherNumber}{(s.InvoiceNumber == null ? string.Empty : " • " + s.InvoiceNumber)}", ct);
        return await GetAsync(d.Id, ct);
    }

    private async Task<(RentalDeposit Deposit, RentalContract Contract)> HeldAsync(Guid id, CancellationToken ct)
    {
        var (d, c) = await LoadAsync(id, ct);
        if (c.Status != RentalContractStatuses.Active) throw new ConflictException(Messages.RentalDepositContractState);
        if (d.Status != DSt.Held || Balance(d) <= 0) throw new ConflictException(Messages.RentalDepositNotHeld);
        return (d, c);
    }

    private async Task<(RentalDeposit Deposit, RentalContract Contract)> LoadAsync(Guid id, CancellationToken ct)
    {
        // قفل صف التأمين يسلسل التسويات المتزامنة على رصيده
        await _db.Set<RentalDeposit>().Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, x => x.Status), ct);
        var d = await _db.Set<RentalDeposit>().Include(x => x.Settlements).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(Messages.RentalDepositNotFound);
        return (d, await ContractAsync(d.ContractId, ct));
    }

    private async Task<RentalContract> ContractAsync(Guid id, CancellationToken ct)
        => await _db.Set<RentalContract>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException(Messages.RentalContractNotFound);

    private async Task<Customer> CustomerAsync(Guid id, CancellationToken ct)
        => await _db.Set<Customer>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new ValidationFailedException(Messages.CustomerNotFound);

    private async Task<string> LiabilityAccountAsync(CancellationToken ct)
    {
        await ContractAccountsSetup.EnsureModuleMissingAsync(_db, ContractAccountsSetup.ModuleAccountSet.Rental, ct);
        var s = await _db.Set<AccountingSettings>().AsNoTracking().FirstAsync(ct);
        return s.RentalDepositsLiabilityAccountCode ?? throw new ValidationFailedException(Messages.RentalAccountsMissing);
    }

    private static decimal Balance(RentalDeposit d) => d.Amount - d.Settlements.Sum(s => s.Amount);

    private static string RequireReason(RentalReasonDto r)
        => Trim(r.Reason, 500) ?? throw new ValidationFailedException(Messages.ReasonRequired);

    private async Task<List<RentalDepositDto>> DtosAsync(List<RentalDeposit> list, CancellationToken ct)
    {
        var contractIds = list.Select(d => d.ContractId).Distinct().ToList();
        var customerIds = list.Select(d => d.CustomerId).Distinct().ToList();
        var contracts = await _db.Set<RentalContract>().AsNoTracking().Where(c => contractIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.ContractNumber, ct);
        var customers = await _db.Set<Customer>().AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.NameAr, ct);
        return list.Select(d => new RentalDepositDto
        {
            Id = d.Id, ContractId = d.ContractId, ContractNumber = contracts.GetValueOrDefault(d.ContractId), CustomerId = d.CustomerId,
            CustomerName = customers.GetValueOrDefault(d.CustomerId), Amount = d.Amount,
            AppliedAmount = d.Settlements.Where(s => s.Kind == SK.Apply).Sum(s => s.Amount),
            RefundedAmount = d.Settlements.Where(s => s.Kind == SK.Refund).Sum(s => s.Amount),
            Balance = d.Status == DSt.Cancelled ? 0 : Balance(d), ReceivedAt = d.ReceivedAt, ReceiptVoucherId = d.ReceiptVoucherId,
            ReceiptVoucherNumber = d.ReceiptVoucherNumber, Status = d.Status, Notes = d.Notes, CancelReason = d.CancelReason, CreatedBy = d.CreatedBy,
            CreatedAt = d.CreatedAt,
            Settlements = d.Settlements.OrderBy(s => s.SettledAt).ThenBy(s => s.CreatedAt).Select(s => new RentalDepositSettlementDto
            {
                Id = s.Id, Kind = s.Kind, Amount = s.Amount, SettledAt = s.SettledAt, InvoiceId = s.InvoiceId, InvoiceNumber = s.InvoiceNumber,
                VoucherId = s.VoucherId, VoucherNumber = s.VoucherNumber, Reason = s.Reason, CreatedBy = s.CreatedBy,
            }).ToList(),
        }).ToList();
    }

    private Task LogAsync(string action, RentalDeposit d, string contractNumber, string details, CancellationToken ct)
        => _audit.LogAsync(action, nameof(RentalDeposit), d.Id.ToString(), $"{contractNumber} • {details}", ct);

    private static string? Trim(string? value, int max)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length > max ? v[..max] : v;
    }
}
