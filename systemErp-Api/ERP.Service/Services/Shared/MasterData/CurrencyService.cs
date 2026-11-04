using System.Text.RegularExpressions;
using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// العملات وأسعار صرفها. السعر يعني: 1 من العملة = السعر من العملة الأساسية (الأساسية دائماً 1).
/// تغيير العملة الأساسية مسموح قبل أول قيد فقط، ويعيد حساب أسعار بقية العملات نسبةً للأساس الجديد
/// ويُحدّث عملة المنشأة والحسابات والبنوك المسجّلة بالأساس القديم.
/// </summary>
public partial class CurrencyService : CrudService<Currency, CurrencyDto, CreateCurrencyDto, UpdateCurrencyDto>, ICurrencyService
{
    public CurrencyService(ErpDbContext db) : base(db) { }
    protected override string Label => Messages.LabelCurrency;

    /// <summary>تغيير الأساس يعدّل عدة جداول معاً.</summary>
    protected override bool Transactional => true;

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex IsoCode();

    protected override async Task ValidateAsync(CreateCurrencyDto d, Currency? existing, CancellationToken ct)
    {
        var code = (d.Code ?? string.Empty).Trim().ToUpperInvariant();
        var errors = new List<string>();
        if (!IsoCode().IsMatch(code)) errors.Add(Messages.CurrencyCodeIso3);
        if (string.IsNullOrWhiteSpace(d.NameAr)) errors.Add(Messages.ArabicNameRequired);
        // موجب دائماً: للعملة المرقّاة هو سعرها مقابل الأساس الحالي ويُعاد به حساب البقية
        if (d.ExchangeRate <= 0) errors.Add(Messages.ExchangeRateMustBePositive);
        if (d.DecimalPlaces is < 0 or > 4) errors.Add(Messages.DecimalPlacesRange);
        if (d.IsBaseCurrency && d.Status == "inactive") errors.Add(Messages.BaseCurrencyMustBeActive);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        if (await Db.Set<Currency>().AnyAsync(c => c.Code == code && (existing == null || c.Id != existing.Id), ct))
            throw new ConflictException(Messages.CurrencyCodeAlreadyExists);
        if (existing is { IsBaseCurrency: true } && !d.IsBaseCurrency)
            throw new ConflictException(Messages.CannotUnsetBaseCurrency);

        // تغيير رمز عملة استُخدمت في مستند أو حساب يقطع الربط بها
        if (existing != null && existing.Code != code && await IsInUseAsync(existing.Code, ct))
            throw new ConflictException(Messages.CurrencyCodeInUse);

        // عملة أساسية جديدة: الأرصدة والقيود مسجّلة بالأساس الحالي، فلا يُغيَّر بعد أول قيد
        var becomesBase = d.IsBaseCurrency && existing is not { IsBaseCurrency: true };
        if (becomesBase && await Db.Set<Currency>().AnyAsync(c => c.IsBaseCurrency, ct) && await Db.Set<JournalEntry>().AnyAsync(ct))
            throw new ConflictException(Messages.CannotChangeBaseCurrencyAfterTransactions);
    }

    protected override async Task OnCreatingAsync(Currency e, CreateCurrencyDto d, CancellationToken ct)
    {
        e.Code = e.Code.Trim().ToUpperInvariant(); e.LastUpdated = DateTime.UtcNow;
        if (e.IsBaseCurrency) await MakeBaseAsync(e, e.ExchangeRate, ct);
    }

    protected override async Task OnUpdatingAsync(Currency e, UpdateCurrencyDto d, CancellationToken ct)
    {
        e.Code = e.Code.Trim().ToUpperInvariant(); e.LastUpdated = DateTime.UtcNow;
        if (!e.IsBaseCurrency) return;
        var entry = Db.Entry(e);
        var wasBase = entry.Property(x => x.IsBaseCurrency).OriginalValue;
        if (wasBase) { e.ExchangeRate = 1; return; }
        // سعرها مقابل الأساس القديم (المرسل مع الطلب، أو المحفوظ) هو معامل إعادة الحساب
        var rate = e.ExchangeRate > 0 ? e.ExchangeRate : entry.Property(x => x.ExchangeRate).OriginalValue;
        await MakeBaseAsync(e, rate, ct);
    }

    /// <summary>
    /// يجعل العملة أساساً: الأسعار تُقسم على سعرها القديم (فالأساس القديم يصبح 1 ÷ سعرها)،
    /// وعملة المنشأة والحسابات والبنوك المسجّلة بالأساس القديم تنتقل للجديد.
    /// </summary>
    private async Task MakeBaseAsync(Currency newBase, decimal rateInOldBase, CancellationToken ct)
    {
        newBase.ExchangeRate = 1;
        var others = await Db.Set<Currency>().Where(c => c.Id != newBase.Id).ToListAsync(ct);
        var oldBase = others.FirstOrDefault(c => c.IsBaseCurrency);
        if (oldBase == null) return; // أول عملة أساسية: لا شيء يُعاد حسابه

        var factor = rateInOldBase > 0 ? rateInOldBase : 1m;
        foreach (var c in others)
        {
            c.IsBaseCurrency = false;
            c.ExchangeRate = Math.Round(c.ExchangeRate / factor, 4, MidpointRounding.AwayFromZero);
            if (c.ExchangeRate <= 0) c.ExchangeRate = 0.0001m;
            c.LastUpdated = DateTime.UtcNow;
        }

        var from = oldBase.Code;
        var to = newBase.Code;
        foreach (var t in await Db.Set<Tenant>().Where(t => t.Currency == from).ToListAsync(ct)) t.Currency = to;
        foreach (var a in await Db.Set<Account>().Where(a => a.Currency == from).ToListAsync(ct)) a.Currency = to;
        foreach (var b in await Db.Set<BankEntity>().Where(b => b.Currency == from).ToListAsync(ct)) b.Currency = to;
    }

    protected override async Task OnDeletingAsync(Currency e, CancellationToken ct)
    {
        if (e.IsBaseCurrency) throw new ConflictException(Messages.CannotDeleteBaseCurrency);
        if (await IsInUseAsync(e.Code, ct)) throw new ConflictException(Messages.CurrencyInUseCannotDelete);
    }

    /// <summary>هل استُخدم الرمز في فاتورة أو مرتجع أو حساب أو بنك؟</summary>
    private async Task<bool> IsInUseAsync(string code, CancellationToken ct)
        => await Db.Set<Invoice>().AnyAsync(x => x.CurrencyCode == code, ct)
        || await Db.Set<SalesInvoice>().AnyAsync(x => x.CurrencyCode == code, ct)
        || await Db.Set<PurchaseInvoice>().AnyAsync(x => x.CurrencyCode == code, ct)
        || await Db.Set<SalesReturn>().AnyAsync(x => x.CurrencyCode == code, ct)
        || await Db.Set<PurchaseReturn>().AnyAsync(x => x.CurrencyCode == code, ct)
        || await Db.Set<Account>().AnyAsync(x => x.Currency == code, ct)
        || await Db.Set<BankEntity>().AnyAsync(x => x.Currency == code, ct);
}
