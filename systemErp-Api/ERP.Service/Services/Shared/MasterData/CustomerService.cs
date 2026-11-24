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
        PartyValidation.AddAddressErrors(extra, d.Country, d.BuildingNo, d.PostalCode, d.AdditionalNo);
        PartyValidation.AddIdentificationErrors(extra, d.IdType, d.IdNumber);
        if (d.IsVatRegistered && string.IsNullOrWhiteSpace(d.VatNumber)) extra.Add(Messages.VatRegisteredPartyNeedsVatNumber);
        PartyValidation.Validate(d.NameAr, d.VatNumber, d.Email, d.OpeningBalance, extra, d.Country);
        if (!string.IsNullOrWhiteSpace(d.Code)
            && await Db.Set<Customer>().AnyAsync(c => c.Code == d.Code && (existing == null || c.Id != existing.Id), ct))
            throw new ConflictException(Messages.CustomerCodeInUse);
    }

    // ---------- الاستيراد من Excel ----------
    public async Task<PartyImportPreviewDto> PreviewImportAsync(List<PartyImportRowInput<CreateCustomerDto>> rows, CancellationToken ct = default)
        => PartyImport.Summarize(await CheckImportAsync(rows, ct));

    public async Task<ImportResultDto> ImportRowsAsync(List<PartyImportRowInput<CreateCustomerDto>> rows, CancellationToken ct = default)
        => await PartyImport.SaveAsync(rows, await CheckImportAsync(rows, ct), CreateAsync, c => c.Id, Label, ct);

    /// <summary>الهاتف إلزامي للعميل؛ والتكرار يُفحص مع العملاء المسجّلين وداخل الملف.</summary>
    private async Task<List<PartyImportRowDto>> CheckImportAsync(List<PartyImportRowInput<CreateCustomerDto>> rows, CancellationToken ct)
    {
        var existing = await Db.Set<Customer>().AsNoTracking()
            .Select(c => new PartyImport.PartyRecord(c.Code, c.NameAr, c.VatNumber, c.CrNumber, c.Phone)).ToListAsync(ct);
        return PartyImport.Check(rows, existing,
            d =>
            {
                d.Code = d.Code?.Trim() ?? string.Empty; d.NameAr = d.NameAr?.Trim() ?? string.Empty;
                d.VatNumber = d.VatNumber?.Trim(); d.CrNumber = d.CrNumber?.Trim();
                return new PartyImport.PartyRecord(d.Code, d.NameAr, d.VatNumber, d.CrNumber, d.Phone);
            },
            (d, phone) => d.Phone = phone ?? string.Empty,
            d =>
            {
                var errors = PartyImport.CommonErrors(d.NameAr, d.VatNumber, d.Email, d.OpeningBalance, d.Country, d.BuildingNo, d.PostalCode, d.AdditionalNo, d.IdType, d.IdNumber);
                if (d.CreditLimit < 0) errors.Add(Messages.CreditLimitCannotBeNegative);
                if (d.CreditPeriodDays < 0) errors.Add(Messages.CreditPeriodCannotBeNegative);
                return errors;
            },
            phoneRequired: true);
    }

    protected override async Task OnCreatingAsync(Customer e, CreateCustomerDto d, CancellationToken ct)
    {
        e.Country = PartyValidation.NormalizeCountry(e.Country);
        if (string.IsNullOrWhiteSpace(e.Code)) e.Code = await _numbers.NextAsync("customer", "CUST-");
        e.CurrentBalance = e.OpeningBalance;
        e.AccountCode = await _accounts.EnsureLinkedAccountAsync(LinkedEntityType.Customer, e.Id, e.NameAr, e.NameEn, d.AccountCode, ct);
        await OpeningBalances.ApplyAsync(_posting, Db, null, id => e.OpeningEntryId = id, e.AccountCode!, e.OpeningBalance, debitNature: true, e.NameAr, e.Id, ct);
    }

    protected override async Task OnUpdatingAsync(Customer e, UpdateCustomerDto d, CancellationToken ct)
    {
        e.Country = PartyValidation.NormalizeCountry(e.Country);
        // أرصدة الحركة لا تُعدَّل من الطلب، وحساب الأستاذ لا يتغيّر إلا عبر قواعد الحساب المرتبط (بلا تاريخ محاسبي)
        var o = Db.Entry(e).OriginalValues;
        e.AccountCode = o.GetValue<string>(nameof(Customer.AccountCode));
        e.AccountCode = await _accounts.ChangeLinkedAccountCodeAsync(LinkedEntityType.Customer, e.Id, e.AccountCode, d.AccountCode, ct) ?? e.AccountCode;
        e.CurrentBalance = o.GetValue<decimal>(nameof(Customer.CurrentBalance));
        if (string.IsNullOrWhiteSpace(e.Code)) e.Code = o.GetValue<string>(nameof(Customer.Code));
        // الرصيد الافتتاحي قيد في الدفاتر: تغييره يعكس قيده ويرحّل الجديد، والطرف القديم يُقيَّد افتتاحيه عند أول تعديل
        e.OpeningEntryId = o.GetValue<Guid?>(nameof(Customer.OpeningEntryId));
        if (e.OpeningBalance != o.GetValue<decimal>(nameof(Customer.OpeningBalance)) || (e.OpeningEntryId == null && e.OpeningBalance != 0))
            await OpeningBalances.ApplyAsync(_posting, Db, e.OpeningEntryId, id => e.OpeningEntryId = id, e.AccountCode!, e.OpeningBalance, debitNature: true, e.NameAr, e.Id, ct);
        // اسم الحساب يتبع اسم العميل، للحساب المملوك له فقط (الحساب العام/المشترك لا يُعاد تسميته)
        if (await LinkedPartyAccounts.OwnedAccountAsync(Db, LinkedPartyAccounts.Of(e), ct) is { } acc)
        {
            acc.NameAr = e.NameAr; acc.NameEn = string.IsNullOrWhiteSpace(e.NameEn) ? e.NameAr : e.NameEn;
        }
    }

    /// <summary>العميل وحسابه المملوك يُحذفان معاً، أو يُرفض الحذف كله إن كان له فواتير أو قيود أو سجلات تشير إليه أو إلى حسابه.</summary>
    protected override async Task OnDeletingAsync(Customer e, CancellationToken ct)
    {
        if (await Db.Set<InvoiceBase>().AnyAsync(i => i.PartyId == e.Id, ct))
            throw new ConflictException(Messages.CannotDeleteCustomerWithInvoices);
        if (await LinkedPartyAccounts.PrepareDeleteAsync(Db, LinkedPartyAccounts.Of(e), Messages.CannotDeleteCustomerWithEntries, ct) is { } acc)
            Db.Remove(acc);
    }
}
