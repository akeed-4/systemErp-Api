using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Accounting;

namespace ERP.Service.Services.Shared;

public class BankService : CrudService<BankEntity, BankEntityDto, CreateBankEntityDto, UpdateBankEntityDto>, IBankService
{
    private readonly IAccountService _accounts;
    private readonly INumberSequenceService _numbers;
    private readonly IAccountingPostingService _posting;

    public BankService(ErpDbContext db, IAccountService accounts, INumberSequenceService numbers, IAccountingPostingService posting) : base(db)
    {
        _accounts = accounts; _numbers = numbers; _posting = posting;
    }

    protected override string Label => Messages.LabelBank;
    protected override bool Transactional => true;

    protected override IQueryable<BankEntity> ApplySearch(IQueryable<BankEntity> q, string t)
        => q.Where(b => b.Code.Contains(t) || b.NameAr.Contains(t) || b.NameEn.Contains(t) || b.Iban.Contains(t) || b.AccountNumber.Contains(t));

    protected override async Task ValidateAsync(CreateBankEntityDto d, BankEntity? existing, CancellationToken ct)
    {
        var extra = new List<string>();
        if (!string.IsNullOrWhiteSpace(d.Iban) && !(d.Iban.Length is >= 15 and <= 34 && d.Iban.All(char.IsLetterOrDigit)))
            extra.Add(Messages.IbanInvalid);
        PartyValidation.AddAddressErrors(extra, d.Country, d.BuildingNo, d.PostalCode, d.AdditionalNo);
        PartyValidation.AddIdentificationErrors(extra, d.IdType, d.IdNumber);
        PartyValidation.Validate(d.NameAr, d.VatNumber, d.Email, d.OpeningBalance, extra, d.Country);
        if (!string.IsNullOrWhiteSpace(d.Code)
            && await Db.Set<BankEntity>().AnyAsync(b => b.Code == d.Code && (existing == null || b.Id != existing.Id), ct))
            throw new ConflictException(Messages.BankCodeInUse);

        // عملة الحساب من شاشة العملات: معرّفة ونشطة (عملة بنك قائم موقوفة لاحقاً تبقى مقبولة ما لم تتغيّر)
        var code = await CurrencyCodeAsync(d.Currency, ct);
        var unchanged = existing != null && string.Equals(existing.Currency, code, StringComparison.OrdinalIgnoreCase);
        if (!unchanged && !await Db.Set<Currency>().AnyAsync(c => c.Code == code && (c.IsBaseCurrency || c.Status != "inactive"), ct))
            throw new ValidationFailedException(string.Format(Messages.CurrencyNotDefinedOrInactive, code));
    }

    /// <summary>عملة البنك بحروف كبيرة، والفارغة = العملة الأساسية.</summary>
    private async Task<string> CurrencyCodeAsync(string? currency, CancellationToken ct)
        => string.IsNullOrWhiteSpace(currency)
            ? await Db.Set<Currency>().Where(c => c.IsBaseCurrency).Select(c => c.Code).FirstOrDefaultAsync(ct) ?? "SAR"
            : currency.Trim().ToUpperInvariant();

    protected override async Task OnCreatingAsync(BankEntity e, CreateBankEntityDto d, CancellationToken ct)
    {
        e.Country = PartyValidation.NormalizeCountry(e.Country);
        e.Currency = await CurrencyCodeAsync(e.Currency, ct);
        if (string.IsNullOrWhiteSpace(e.Code)) e.Code = await _numbers.NextAsync("bank", "BNK-");
        e.CurrentBalance = e.OpeningBalance;
        e.AccountCode = await _accounts.EnsureLinkedAccountAsync(LinkedEntityType.Bank, e.Id, e.NameAr, e.NameEn, ct);
        // حساب البنك بعملته (كشف الحساب يعرض الرصيد بها)
        var account = Db.Set<Account>().Local.FirstOrDefault(a => a.Code == e.AccountCode)
            ?? await Db.Set<Account>().FirstOrDefaultAsync(a => a.Code == e.AccountCode, ct);
        if (account != null) account.Currency = e.Currency;
        await OpeningBalances.ApplyAsync(_posting, Db, null, id => e.OpeningEntryId = id, e.AccountCode!, e.OpeningBalance, debitNature: true, e.NameAr, e.Id, ct);
    }

    protected override async Task OnUpdatingAsync(BankEntity e, UpdateBankEntityDto d, CancellationToken ct)
    {
        e.Country = PartyValidation.NormalizeCountry(e.Country);
        e.Currency = await CurrencyCodeAsync(e.Currency, ct);
        var o = Db.Entry(e).OriginalValues;
        e.AccountCode = o.GetValue<string?>(nameof(BankEntity.AccountCode));
        e.CurrentBalance = o.GetValue<decimal>(nameof(BankEntity.CurrentBalance));
        if (string.IsNullOrWhiteSpace(e.Code)) e.Code = o.GetValue<string>(nameof(BankEntity.Code));
        // الرصيد الافتتاحي قيد في الدفاتر: تغييره يعكس قيده ويرحّل الجديد، والطرف القديم يُقيَّد افتتاحيه عند أول تعديل
        e.OpeningEntryId = o.GetValue<Guid?>(nameof(BankEntity.OpeningEntryId));
        if (e.OpeningBalance != o.GetValue<decimal>(nameof(BankEntity.OpeningBalance)) || (e.OpeningEntryId == null && e.OpeningBalance != 0))
            await OpeningBalances.ApplyAsync(_posting, Db, e.OpeningEntryId, id => e.OpeningEntryId = id, e.AccountCode!, e.OpeningBalance, debitNature: true, e.NameAr, e.Id, ct);
        var acc = await Db.Set<Account>().FirstOrDefaultAsync(a => a.Code == e.AccountCode, ct);
        if (acc != null) { acc.NameAr = e.NameAr; acc.NameEn = string.IsNullOrWhiteSpace(e.NameEn) ? e.NameAr : e.NameEn; acc.Currency = e.Currency; }
    }

    protected override async Task OnDeletingAsync(BankEntity e, CancellationToken ct)
    {
        var acc = await Db.Set<Account>().FirstOrDefaultAsync(a => a.Code == e.AccountCode, ct);
        if (acc != null)
        {
            if (await Db.Set<JournalEntryLine>().AnyAsync(l => l.AccountCode == acc.Code, ct))
                throw new ConflictException(Messages.CannotDeleteBankWithEntries);
            Db.Remove(acc);
        }
    }
}
