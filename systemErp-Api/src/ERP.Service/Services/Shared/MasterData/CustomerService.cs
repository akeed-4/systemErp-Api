using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

public class CustomerService : CrudService<Customer, CustomerDto, CreateCustomerDto, UpdateCustomerDto>, ICustomerService
{
    private readonly IAccountService _accounts;
    private readonly INumberSequenceService _numbers;

    public CustomerService(ErpDbContext db, IAccountService accounts, INumberSequenceService numbers) : base(db)
    {
        _accounts = accounts; _numbers = numbers;
    }

    protected override string Label => "العميل";
    protected override bool Transactional => true;

    protected override IQueryable<Customer> ApplySearch(IQueryable<Customer> q, string t)
        => q.Where(c => c.Code.Contains(t) || c.NameAr.Contains(t) || c.NameEn.Contains(t) || c.Phone.Contains(t) || (c.VatNumber != null && c.VatNumber.Contains(t)));

    protected override IQueryable<Customer> ApplyFilters(IQueryable<Customer> q, PaginationParams p)
        => string.IsNullOrWhiteSpace(p.Status) ? q : q.Where(c => c.Status == p.Status);

    protected override async Task ValidateAsync(CreateCustomerDto d, Customer? existing, CancellationToken ct)
    {
        var extra = new List<string>();
        if (d.CreditLimit < 0) extra.Add("الحد الائتماني لا يكون سالباً.");
        if (d.CreditPeriodDays < 0) extra.Add("مدة الائتمان لا تكون سالبة.");
        PartyValidation.Validate(d.NameAr, d.VatNumber, d.Email, d.OpeningBalance, extra);
        if (!string.IsNullOrWhiteSpace(d.Code)
            && await Db.Set<Customer>().AnyAsync(c => c.Code == d.Code && (existing == null || c.Id != existing.Id), ct))
            throw new ConflictException("كود العميل مستخدم مسبقاً.");
    }

    protected override async Task OnCreatingAsync(Customer e, CreateCustomerDto d, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(e.Code)) e.Code = await _numbers.NextAsync("customer", "CUST-");
        e.CurrentBalance = e.OpeningBalance;
        e.AccountCode = await _accounts.EnsureLinkedAccountAsync(LinkedEntityType.Customer, e.Id, e.NameAr, e.NameEn, ct);
    }

    protected override async Task OnUpdatingAsync(Customer e, UpdateCustomerDto d, CancellationToken ct)
    {
        // الكود وحساب الأستاذ وأرصدة الحركة لا يُعدَّلان من الطلب.
        var o = Db.Entry(e).OriginalValues;
        e.AccountCode = o.GetValue<string>(nameof(Customer.AccountCode));
        e.CurrentBalance = o.GetValue<decimal>(nameof(Customer.CurrentBalance));
        if (string.IsNullOrWhiteSpace(e.Code)) e.Code = o.GetValue<string>(nameof(Customer.Code));
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
            throw new ConflictException("لا يمكن حذف عميل له فواتير.");
        var acc = await Db.Set<Account>().FirstOrDefaultAsync(a => a.Code == e.AccountCode, ct);
        if (acc != null)
        {
            if (await Db.Set<JournalEntryLine>().AnyAsync(l => l.AccountCode == acc.Code, ct))
                throw new ConflictException("لا يمكن حذف عميل عليه حركات محاسبية.");
            Db.Remove(acc);
        }
    }
}
