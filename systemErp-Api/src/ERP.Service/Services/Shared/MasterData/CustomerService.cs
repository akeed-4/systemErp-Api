using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Accounting;

namespace ERP.Service.Services.Shared;

public class CustomerService : CrudService<Customer, CustomerDto, CreateCustomerDto, UpdateCustomerDto>, ICustomerService
{
    private readonly IAccountService _accounts;
    private readonly INumberSequenceService _numbers;
    private readonly IAccountingPostingService _posting;

    public CustomerService(ErpDbContext db, IAccountService accounts, INumberSequenceService numbers, IAccountingPostingService posting) : base(db)
    {
        _accounts = accounts; _numbers = numbers; _posting = posting;
    }

    protected override string Label => Messages.PartyCustomer;
    protected override bool Transactional => true;

    protected override IQueryable<Customer> ApplySearch(IQueryable<Customer> q, string t)
        => q.Where(c => c.Code.Contains(t) || c.NameAr.Contains(t) || c.NameEn.Contains(t) || c.Phone.Contains(t) || (c.VatNumber != null && c.VatNumber.Contains(t)));

    protected override IQueryable<Customer> ApplyFilters(IQueryable<Customer> q, PaginationParams p)
        => string.IsNullOrWhiteSpace(p.Status) ? q : q.Where(c => c.Status == p.Status);

    protected override async Task ValidateAsync(CreateCustomerDto d, Customer? existing, CancellationToken ct)
    {
        var extra = new List<string>();
        if (d.CreditLimit < 0) extra.Add(Messages.CreditLimitCannotBeNegative);
        if (d.CreditPeriodDays < 0) extra.Add(Messages.CreditPeriodCannotBeNegative);
        PartyValidation.Validate(d.NameAr, d.VatNumber, d.Email, d.OpeningBalance, extra);
        if (!string.IsNullOrWhiteSpace(d.Code)
            && await Db.Set<Customer>().AnyAsync(c => c.Code == d.Code && (existing == null || c.Id != existing.Id), ct))
            throw new ConflictException(Messages.CustomerCodeInUse);
    }

    protected override async Task OnCreatingAsync(Customer e, CreateCustomerDto d, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(e.Code)) e.Code = await _numbers.NextAsync("customer", "CUST-");
        e.CurrentBalance = e.OpeningBalance;
        e.AccountCode = await _accounts.EnsureLinkedAccountAsync(LinkedEntityType.Customer, e.Id, e.NameAr, e.NameEn, ct);
        await OpeningBalances.ApplyAsync(_posting, Db, null, id => e.OpeningEntryId = id, e.AccountCode!, e.OpeningBalance, debitNature: true, e.NameAr, e.Id, ct);
    }

    protected override async Task OnUpdatingAsync(Customer e, UpdateCustomerDto d, CancellationToken ct)
    {
        // الكود وحساب الأستاذ وأرصدة الحركة لا يُعدَّلان من الطلب.
        var o = Db.Entry(e).OriginalValues;
        e.AccountCode = o.GetValue<string>(nameof(Customer.AccountCode));
        e.CurrentBalance = o.GetValue<decimal>(nameof(Customer.CurrentBalance));
        if (string.IsNullOrWhiteSpace(e.Code)) e.Code = o.GetValue<string>(nameof(Customer.Code));
        // الرصيد الافتتاحي قيد في الدفاتر: تغييره يعكس قيده ويرحّل الجديد، والطرف القديم يُقيَّد افتتاحيه عند أول تعديل
        e.OpeningEntryId = o.GetValue<Guid?>(nameof(Customer.OpeningEntryId));
        if (e.OpeningBalance != o.GetValue<decimal>(nameof(Customer.OpeningBalance)) || (e.OpeningEntryId == null && e.OpeningBalance != 0))
            await OpeningBalances.ApplyAsync(_posting, Db, e.OpeningEntryId, id => e.OpeningEntryId = id, e.AccountCode!, e.OpeningBalance, debitNature: true, e.NameAr, e.Id, ct);
        await RenameLinkedAccountAsync(e.AccountCode, e.NameAr, e.NameEn, ct);
    }

    private async Task RenameLinkedAccountAsync(string code, string ar, string en, CancellationToken ct)
    {
        var acc = await Db.Set<Account>().FirstOrDefaultAsync(a => a.Code == code, ct);
        if (acc != null) { acc.NameAr = ar; acc.NameEn = string.IsNullOrWhiteSpace(en) ? ar : en; }
    }

    protected override async Task OnDeletingAsync(Customer e, CancellationToken ct)
    {
        if (await Db.Set<Invoice>().AnyAsync(i => i.PartyId == e.Id, ct))
            throw new ConflictException(Messages.CannotDeleteCustomerWithInvoices);
        var acc = await Db.Set<Account>().FirstOrDefaultAsync(a => a.Code == e.AccountCode, ct);
        if (acc != null)
        {
            if (await Db.Set<JournalEntryLine>().AnyAsync(l => l.AccountCode == acc.Code, ct))
                throw new ConflictException(Messages.CannotDeleteCustomerWithEntries);
            Db.Remove(acc);
        }
    }
}
