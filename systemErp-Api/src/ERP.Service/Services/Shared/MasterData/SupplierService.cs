using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

public class SupplierService : CrudService<Supplier, SupplierDto, CreateSupplierDto, UpdateSupplierDto>, ISupplierService
{
    private readonly IAccountService _accounts;
    private readonly INumberSequenceService _numbers;

    public SupplierService(ErpDbContext db, IAccountService accounts, INumberSequenceService numbers) : base(db)
    {
        _accounts = accounts; _numbers = numbers;
    }

    protected override string Label => "المورد";
    protected override bool Transactional => true;

    protected override IQueryable<Supplier> ApplySearch(IQueryable<Supplier> q, string t)
        => q.Where(s => s.Code.Contains(t) || s.NameAr.Contains(t) || s.NameEn.Contains(t) || (s.Phone != null && s.Phone.Contains(t)) || (s.VatNumber != null && s.VatNumber.Contains(t)));

    protected override IQueryable<Supplier> ApplyFilters(IQueryable<Supplier> q, PaginationParams p)
        => string.IsNullOrWhiteSpace(p.Status) ? q : q.Where(s => s.Status == p.Status);

    protected override async Task ValidateAsync(CreateSupplierDto d, Supplier? existing, CancellationToken ct)
    {
        var extra = new List<string>();
        if (d.PaymentTermsDays < 0) extra.Add("مدة السداد لا تكون سالبة.");
        PartyValidation.Validate(d.NameAr, d.VatNumber, d.Email, d.OpeningBalance, extra);
        if (!string.IsNullOrWhiteSpace(d.Code)
            && await Db.Set<Supplier>().AnyAsync(s => s.Code == d.Code && (existing == null || s.Id != existing.Id), ct))
            throw new ConflictException("كود المورد مستخدم مسبقاً.");
    }

    protected override async Task OnCreatingAsync(Supplier e, CreateSupplierDto d, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(e.Code)) e.Code = await _numbers.NextAsync("supplier", "SUP-");
        e.CurrentBalance = e.OpeningBalance;
        e.AccountCode = await _accounts.EnsureLinkedAccountAsync(LinkedEntityType.Supplier, e.Id, e.NameAr, e.NameEn, ct);
    }

    protected override async Task OnUpdatingAsync(Supplier e, UpdateSupplierDto d, CancellationToken ct)
    {
        var o = Db.Entry(e).OriginalValues;
        e.AccountCode = o.GetValue<string>(nameof(Supplier.AccountCode));
        e.CurrentBalance = o.GetValue<decimal>(nameof(Supplier.CurrentBalance));
        if (string.IsNullOrWhiteSpace(e.Code)) e.Code = o.GetValue<string>(nameof(Supplier.Code));
        var acc = await Db.Set<Account>().FirstOrDefaultAsync(a => a.Code == e.AccountCode, ct);
        if (acc != null) { acc.NameAr = e.NameAr; acc.NameEn = string.IsNullOrWhiteSpace(e.NameEn) ? e.NameAr : e.NameEn; }
    }

    protected override async Task OnDeletingAsync(Supplier e, CancellationToken ct)
    {
        if (await Db.Set<CarProcurementOrder>().AnyAsync(o => o.SupplierId == e.Id, ct))
            throw new ConflictException("لا يمكن حذف مورد له طلبات شراء.");
        var acc = await Db.Set<Account>().FirstOrDefaultAsync(a => a.Code == e.AccountCode, ct);
        if (acc != null)
        {
            if (await Db.Set<JournalEntryLine>().AnyAsync(l => l.AccountCode == acc.Code, ct))
                throw new ConflictException("لا يمكن حذف مورد عليه حركات محاسبية.");
            Db.Remove(acc);
        }
    }
}
