using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

public class BankService : CrudService<BankEntity, BankEntityDto, CreateBankEntityDto, UpdateBankEntityDto>, IBankService
{
    private readonly IAccountService _accounts;
    private readonly INumberSequenceService _numbers;

    public BankService(ErpDbContext db, IAccountService accounts, INumberSequenceService numbers) : base(db)
    {
        _accounts = accounts; _numbers = numbers;
    }

    protected override string Label => "البنك";
    protected override bool Transactional => true;

    protected override IQueryable<BankEntity> ApplySearch(IQueryable<BankEntity> q, string t)
        => q.Where(b => b.Code.Contains(t) || b.NameAr.Contains(t) || b.NameEn.Contains(t) || b.Iban.Contains(t) || b.AccountNumber.Contains(t));

    protected override async Task ValidateAsync(CreateBankEntityDto d, BankEntity? existing, CancellationToken ct)
    {
        var extra = new List<string>();
        if (!string.IsNullOrWhiteSpace(d.Iban) && !(d.Iban.Length is >= 15 and <= 34 && d.Iban.All(char.IsLetterOrDigit)))
            extra.Add("رقم الآيبان غير صالح.");
        PartyValidation.Validate(d.NameAr, null, null, d.OpeningBalance, extra);
        if (!string.IsNullOrWhiteSpace(d.Code)
            && await Db.Set<BankEntity>().AnyAsync(b => b.Code == d.Code && (existing == null || b.Id != existing.Id), ct))
            throw new ConflictException("كود البنك مستخدم مسبقاً.");
    }

    protected override async Task OnCreatingAsync(BankEntity e, CreateBankEntityDto d, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(e.Code)) e.Code = await _numbers.NextAsync("bank", "BNK-");
        e.CurrentBalance = e.OpeningBalance;
        e.AccountCode = await _accounts.EnsureLinkedAccountAsync(LinkedEntityType.Bank, e.Id, e.NameAr, e.NameEn, ct);
    }

    protected override async Task OnUpdatingAsync(BankEntity e, UpdateBankEntityDto d, CancellationToken ct)
    {
        var o = Db.Entry(e).OriginalValues;
        e.AccountCode = o.GetValue<string?>(nameof(BankEntity.AccountCode));
        e.CurrentBalance = o.GetValue<decimal>(nameof(BankEntity.CurrentBalance));
        if (string.IsNullOrWhiteSpace(e.Code)) e.Code = o.GetValue<string>(nameof(BankEntity.Code));
        var acc = await Db.Set<Account>().FirstOrDefaultAsync(a => a.Code == e.AccountCode, ct);
        if (acc != null) { acc.NameAr = e.NameAr; acc.NameEn = string.IsNullOrWhiteSpace(e.NameEn) ? e.NameAr : e.NameEn; }
    }

    protected override async Task OnDeletingAsync(BankEntity e, CancellationToken ct)
    {
        var acc = await Db.Set<Account>().FirstOrDefaultAsync(a => a.Code == e.AccountCode, ct);
        if (acc != null)
        {
            if (await Db.Set<JournalEntryLine>().AnyAsync(l => l.AccountCode == acc.Code, ct))
                throw new ConflictException("لا يمكن حذف بنك عليه حركات محاسبية.");
            Db.Remove(acc);
        }
    }
}
