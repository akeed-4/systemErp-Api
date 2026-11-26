using System.Text.RegularExpressions;
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Data.ResponseModel;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// قائمة دول المنشأة. تُعبّأ أول مرة بدول العالم (CountrySeed) عند أول طلب للقائمة، ثم تُدار من شاشة الدول.
/// الرمز الثنائي هو المحفوظ في العملاء والموردين والبنوك والمنشأة والفواتير: لا يُغيَّر ولا تُحذف دولة مستخدمة (تُوقف بدلاً من ذلك).
/// </summary>
public partial class CountryService : CrudService<Country, CountryDto, CreateCountryDto, UpdateCountryDto>, ICountryService
{
    public CountryService(ErpDbContext db) : base(db) { }
    protected override string Label => Messages.LabelCountry;

    [GeneratedRegex("^[A-Z]{2}$")] private static partial Regex Alpha2();
    [GeneratedRegex("^[A-Z]{3}$")] private static partial Regex Alpha3();
    [GeneratedRegex(@"^\+\d{1,4}(-\d{1,4})?$")] private static partial Regex DialCodePattern();

    // ---------- القائمة (مع التعبئة الأولى) ----------
    public override async Task<PagedResult<CountryDto>> ListAsync(PaginationParams p, CancellationToken ct = default)
    {
        await EnsureSeededAsync(ct);
        return await base.ListAsync(p, ct);
    }

    public override async Task<LoadResult> LoadAsync(DataSourceLoadOptions options, CancellationToken ct = default)
    {
        await EnsureSeededAsync(ct);
        return await base.LoadAsync(options, ct);
    }

    public async Task<List<CountryDto>> LookupAsync(CancellationToken ct = default)
    {
        await EnsureSeededAsync(ct);
        var rows = await Db.Set<Country>().AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.NameAr).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    /// <summary>منشأة بلا دول بعد: تُضاف دول العالم. طلبان متزامنان قد يتسابقان؛ الفهرس الفريد يرد الثاني فيُتجاهل.</summary>
    private async Task EnsureSeededAsync(CancellationToken ct)
    {
        if (await Db.Set<Country>().AnyAsync(ct)) return;
        var rows = CountrySeed.All.Select(r => new Country
        {
            Code = r.Code, Code3 = r.Code3, NameAr = r.NameAr, NameEn = r.NameEn, DialCode = r.DialCode,
            CurrencyCode = r.Currency, NationalityAr = r.NationalityAr, NationalityEn = r.NationalityEn, IsActive = true,
        }).ToList();
        Db.Set<Country>().AddRange(rows);
        try
        {
            await Db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            foreach (var r in rows) Db.Entry(r).State = EntityState.Detached;
        }
    }

    protected override IQueryable<Country> ApplySearch(IQueryable<Country> query, string term)
        => query.Where(c => c.Code.Contains(term) || (c.Code3 != null && c.Code3.Contains(term)) || c.NameAr.Contains(term)
            || c.NameEn.Contains(term) || (c.DialCode != null && c.DialCode.Contains(term)) || (c.NationalityAr != null && c.NationalityAr.Contains(term)));

    // ---------- التحقق ----------
    protected override async Task ValidateAsync(CreateCountryDto d, Country? existing, CancellationToken ct)
    {
        Normalize(d);
        var errors = new List<string>();
        if (!Alpha2().IsMatch(d.Code)) errors.Add(Messages.CountryCodeIso2);
        if (d.Code3 != null && !Alpha3().IsMatch(d.Code3)) errors.Add(Messages.CountryCode3Invalid);
        if (string.IsNullOrWhiteSpace(d.NameAr)) errors.Add(Messages.ArabicNameRequired);
        if (d.DialCode != null && !DialCodePattern().IsMatch(d.DialCode)) errors.Add(Messages.CountryDialCodeInvalid);
        if (d.CurrencyCode != null && !Alpha3().IsMatch(d.CurrencyCode)) errors.Add(Messages.CountryCurrencyInvalid);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        if (await Db.Set<Country>().AnyAsync(c => c.Code == d.Code && (existing == null || c.Id != existing.Id), ct))
            throw new ConflictException(Messages.CountryCodeAlreadyExists);
        // الرمز محفوظ في السجلات: تغييره لدولة مستخدمة يقطع ربطها
        if (existing != null && existing.Code != d.Code && await IsInUseAsync(existing.Code, ct))
            throw new ConflictException(Messages.CountryCodeInUse);
    }

    /// <summary>حروف كبيرة للرموز، ومفتاح الاتصال بـ + في أوله، والحقول الاختيارية الفارغة null.</summary>
    private static void Normalize(CreateCountryDto d)
    {
        static string? Opt(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        d.Code = (d.Code ?? string.Empty).Trim().ToUpperInvariant();
        d.Code3 = Opt(d.Code3)?.ToUpperInvariant();
        d.NameAr = (d.NameAr ?? string.Empty).Trim();
        d.NameEn = (d.NameEn ?? string.Empty).Trim();
        d.NationalityAr = Opt(d.NationalityAr);
        d.NationalityEn = Opt(d.NationalityEn);
        d.CurrencyCode = Opt(d.CurrencyCode)?.ToUpperInvariant();
        var dial = Opt(d.DialCode)?.Replace(" ", string.Empty);
        d.DialCode = dial == null ? null : dial.StartsWith('+') ? dial : "+" + dial.TrimStart('0');
    }

    protected override async Task OnDeletingAsync(Country e, CancellationToken ct)
    {
        if (await IsInUseAsync(e.Code, ct)) throw new ConflictException(Messages.CountryInUseCannotDelete);
    }

    /// <summary>هل الرمز محفوظ في عميل أو مورد أو بنك أو المنشأة أو فاتورة أو مرتجع؟</summary>
    private async Task<bool> IsInUseAsync(string code, CancellationToken ct)
        => await Db.Set<Customer>().AnyAsync(x => x.Country == code, ct)
        || await Db.Set<Supplier>().AnyAsync(x => x.Country == code, ct)
        || await Db.Set<BankEntity>().AnyAsync(x => x.Country == code, ct)
        || await Db.Set<Tenant>().AnyAsync(x => x.Country == code, ct)
        || await Db.Set<Invoice>().AnyAsync(x => x.PartyCountry == code, ct)
        || await Db.Set<SalesInvoice>().AnyAsync(x => x.PartyCountry == code, ct)
        || await Db.Set<PurchaseInvoice>().AnyAsync(x => x.PartyCountry == code, ct)
        || await Db.Set<SalesReturn>().AnyAsync(x => x.PartyCountry == code, ct)
        || await Db.Set<PurchaseReturn>().AnyAsync(x => x.PartyCountry == code, ct);
}
