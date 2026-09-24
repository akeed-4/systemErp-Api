using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

public class CurrencyService : CrudService<Currency, CurrencyDto, CreateCurrencyDto, UpdateCurrencyDto>, ICurrencyService
{
    public CurrencyService(ErpDbContext db) : base(db) { }
    protected override string Label => "العملة";

    protected override async Task ValidateAsync(CreateCurrencyDto d, Currency? existing, CancellationToken ct)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(d.Code) || d.Code.Length != 3) errors.Add("رمز العملة ISO من 3 أحرف.");
        if (string.IsNullOrWhiteSpace(d.NameAr)) errors.Add("الاسم بالعربية مطلوب.");
        if (d.ExchangeRate <= 0) errors.Add("سعر الصرف يجب أن يكون موجباً.");
        if (d.DecimalPlaces is < 0 or > 4) errors.Add("خانات الكسور بين 0 و4.");
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        if (await Db.Set<Currency>().AnyAsync(c => c.Code == d.Code.ToUpper() && (existing == null || c.Id != existing.Id), ct))
            throw new ConflictException("رمز العملة مسجّل مسبقاً.");
        if (existing is { IsBaseCurrency: true } && !d.IsBaseCurrency)
            throw new ConflictException("لا يمكن إلغاء العملة الأساسية؛ عيّن عملة أخرى كأساسية أولاً.");
    }

    protected override async Task OnCreatingAsync(Currency e, CreateCurrencyDto d, CancellationToken ct)
    {
        e.Code = e.Code.ToUpper(); e.LastUpdated = DateTime.UtcNow;
        if (e.IsBaseCurrency) { e.ExchangeRate = 1; await ClearOtherBaseAsync(e.Id, ct); }
    }

    protected override async Task OnUpdatingAsync(Currency e, UpdateCurrencyDto d, CancellationToken ct)
    {
        e.Code = e.Code.ToUpper(); e.LastUpdated = DateTime.UtcNow;
        if (e.IsBaseCurrency) { e.ExchangeRate = 1; await ClearOtherBaseAsync(e.Id, ct); }
    }

    private async Task ClearOtherBaseAsync(Guid keepId, CancellationToken ct)
    {
        foreach (var other in await Db.Set<Currency>().Where(c => c.IsBaseCurrency && c.Id != keepId).ToListAsync(ct))
            other.IsBaseCurrency = false;
    }

    protected override Task OnDeletingAsync(Currency e, CancellationToken ct)
        => e.IsBaseCurrency ? throw new ConflictException("لا يمكن حذف العملة الأساسية.") : Task.CompletedTask;
}
