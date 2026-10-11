using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

public class CurrencyService : CrudService<Currency, CurrencyDto, CreateCurrencyDto, UpdateCurrencyDto>, ICurrencyService
{
    public CurrencyService(ErpDbContext db) : base(db) { }
    protected override string Label => Messages.LabelCurrency;

    protected override async Task ValidateAsync(CreateCurrencyDto d, Currency? existing, CancellationToken ct)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(d.Code) || d.Code.Length != 3) errors.Add(Messages.CurrencyCodeIso3);
        if (string.IsNullOrWhiteSpace(d.NameAr)) errors.Add(Messages.ArabicNameRequired);
        if (d.ExchangeRate <= 0) errors.Add(Messages.ExchangeRateMustBePositive);
        if (d.DecimalPlaces is < 0 or > 4) errors.Add(Messages.DecimalPlacesRange);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        if (await Db.Set<Currency>().AnyAsync(c => c.Code == d.Code.ToUpper() && (existing == null || c.Id != existing.Id), ct))
            throw new ConflictException(Messages.CurrencyCodeAlreadyExists);
        if (existing is { IsBaseCurrency: true } && !d.IsBaseCurrency)
            throw new ConflictException(Messages.CannotUnsetBaseCurrency);
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
        => e.IsBaseCurrency ? throw new ConflictException(Messages.CannotDeleteBaseCurrency) : Task.CompletedTask;
}
