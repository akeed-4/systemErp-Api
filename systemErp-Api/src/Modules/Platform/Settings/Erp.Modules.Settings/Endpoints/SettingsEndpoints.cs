using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.BuildingBlocks.Web;
using Erp.Modules.Settings.Application;
using Erp.Modules.Settings.Persistence;
using Erp.SharedKernel.Errors;
using Erp.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Settings.Endpoints;

internal sealed record TenantSettingsDto(
    string DefaultLanguage,
    decimal DefaultVatRate,
    bool IsCarShowroomEnabled,
    bool IsPosEnabled,
    bool IsGeneralTradingEnabled);

internal sealed record SaveCurrencyRequest(
    string? Code,
    string NameAr,
    string NameEn,
    string Symbol,
    decimal ExchangeRate,
    int? DecimalPlaces,
    string? Status);

internal sealed record DocumentSequenceDto(Guid Id, string DocumentType, Guid? ScopeId, int Year, string Prefix, long NextNumber, int Padding);

internal sealed record UpdateSequenceRequest(string Prefix, int Padding, long? NextNumber);

internal static class SettingsEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var settings = app.MapGroup("/api/v1/settings").WithTags("Settings");
        settings.MapGet(string.Empty, GetSettingsAsync).RequireAuthorization();
        settings.MapPut(string.Empty, UpdateSettingsAsync).RequireRoles(SystemRoles.Owner, SystemRoles.Admin);

        var currencies = app.MapGroup("/api/v1/currencies").WithTags("Settings");
        currencies.MapGet(string.Empty, ListCurrenciesAsync).RequireAuthorization();
        currencies.MapGet("{id:guid}", GetCurrencyAsync).RequireAuthorization();
        currencies.MapPost(string.Empty, CreateCurrencyAsync).RequireScreen(ScreenIds.MasterData, ScreenAction.Create);
        currencies.MapPut("{id:guid}", UpdateCurrencyAsync).RequireScreen(ScreenIds.MasterData, ScreenAction.Edit);
        currencies.MapDelete("{id:guid}", DeleteCurrencyAsync).RequireScreen(ScreenIds.MasterData, ScreenAction.Delete);

        var sequences = app.MapGroup("/api/v1/document-sequences").WithTags("Settings").RequireRoles(SystemRoles.Owner, SystemRoles.Admin);
        sequences.MapGet(string.Empty, ListSequencesAsync);
        sequences.MapPut("{id:guid}", UpdateSequenceAsync);
    }

    private static async Task<IResult> GetSettingsAsync(SettingsDbContext db, CancellationToken ct) =>
        ErpResults.Ok(ToDto(await db.TenantSettings.AsNoTracking().SingleOrDefaultAsync(ct) ?? throw SettingsNotFound()));

    private static async Task<IResult> UpdateSettingsAsync(TenantSettingsDto request, SettingsDbContext db, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        if (request.DefaultLanguage is not ("ar" or "en") || request.DefaultVatRate is < 0 or > 100)
        {
            throw ErpException.Validation("Language must be ar/en and VAT between 0 and 100.", "اللغة يجب أن تكون ar أو en ونسبة الضريبة بين 0 و 100.");
        }

        var settings = await db.TenantSettings.SingleOrDefaultAsync(ct) ?? throw SettingsNotFound();
        settings.Update(request.DefaultLanguage, request.DefaultVatRate, request.IsCarShowroomEnabled, request.IsPosEnabled, request.IsGeneralTradingEnabled);
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Ok(ToDto(settings), "تم حفظ الإعدادات");
    }

    private static async Task<IResult> ListCurrenciesAsync(SettingsDbContext db, CancellationToken ct)
    {
        var list = await db.Currencies.AsNoTracking().OrderByDescending(c => c.IsBaseCurrency).ThenBy(c => c.Code).ToListAsync(ct);
        return ErpResults.Ok(list.Select(CurrencyLookup.ToDto).ToList());
    }

    private static async Task<IResult> GetCurrencyAsync(Guid id, SettingsDbContext db, CancellationToken ct) =>
        ErpResults.Ok(CurrencyLookup.ToDto(await db.Currencies.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw CurrencyNotFound()));

    private static async Task<IResult> CreateCurrencyAsync(SaveCurrencyRequest request, SettingsDbContext db, IUnitOfWork unitOfWork, TimeProvider clock, CancellationToken ct)
    {
        Validate(request, requireCode: true);
        var code = request.Code!.Trim().ToUpperInvariant();
        if (await db.Currencies.AnyAsync(c => c.Code == code, ct))
        {
            throw ErpException.Conflict("currency_exists", $"Currency {code} already exists.", $"العملة {code} موجودة مسبقاً.");
        }

        var currency = new Currency(code, request.NameAr, request.NameEn, request.Symbol, isBase: false, request.ExchangeRate, request.DecimalPlaces ?? 2, clock.GetUtcNow());
        db.Currencies.Add(currency);
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Created($"/api/v1/currencies/{currency.Id}", CurrencyLookup.ToDto(currency), "تم إضافة العملة");
    }

    private static async Task<IResult> UpdateCurrencyAsync(Guid id, SaveCurrencyRequest request, SettingsDbContext db, IUnitOfWork unitOfWork, TimeProvider clock, CancellationToken ct)
    {
        Validate(request, requireCode: false);
        var currency = await db.Currencies.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw CurrencyNotFound();
        var status = string.Equals(request.Status, "inactive", StringComparison.OrdinalIgnoreCase) && !currency.IsBaseCurrency
            ? RecordStatus.Inactive
            : RecordStatus.Active;
        currency.Update(request.NameAr, request.NameEn, request.Symbol, request.ExchangeRate, request.DecimalPlaces ?? currency.DecimalPlaces, status, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Ok(CurrencyLookup.ToDto(currency), "تم تحديث العملة");
    }

    private static async Task<IResult> DeleteCurrencyAsync(Guid id, SettingsDbContext db, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var currency = await db.Currencies.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw CurrencyNotFound();
        if (currency.IsBaseCurrency)
        {
            throw ErpException.Conflict("base_currency_required", "The base currency cannot be deleted.", "لا يمكن حذف العملة الأساسية.");
        }

        db.Currencies.Remove(currency);
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Ok(CurrencyLookup.ToDto(currency), "تم حذف العملة");
    }

    private static async Task<IResult> ListSequencesAsync(SettingsDbContext db, CancellationToken ct) =>
        ErpResults.Ok(await db.DocumentSequences.AsNoTracking()
            .OrderBy(s => s.DocumentType).ThenByDescending(s => s.Year)
            .Select(s => new DocumentSequenceDto(s.Id, s.DocumentType, s.ScopeId, s.Year, s.Prefix, s.NextNumber, s.Padding))
            .ToListAsync(ct));

    private static async Task<IResult> UpdateSequenceAsync(Guid id, UpdateSequenceRequest request, SettingsDbContext db, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        if (request.Padding is < 1 or > 12 || string.IsNullOrWhiteSpace(request.Prefix) || request.Prefix.Length > 40)
        {
            throw ErpException.Validation("Prefix (max 40) and padding (1-12) are required.", "البادئة (40 حرفاً كحد أقصى) وعدد الخانات (1-12) مطلوبة.");
        }

        var sequence = await db.DocumentSequences.SingleOrDefaultAsync(s => s.Id == id, ct) ?? throw ErpException.NotFound("Sequence", "التسلسل");
        sequence.Configure(request.Prefix, request.Padding, request.NextNumber);
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Ok(new DocumentSequenceDto(sequence.Id, sequence.DocumentType, sequence.ScopeId, sequence.Year, sequence.Prefix, sequence.NextNumber, sequence.Padding));
    }

    private static TenantSettingsDto ToDto(TenantSettings s) =>
        new(s.DefaultLanguage, s.DefaultVatRate, s.IsCarShowroomEnabled, s.IsPosEnabled, s.IsGeneralTradingEnabled);

    private static void Validate(SaveCurrencyRequest request, bool requireCode)
    {
        var codeOk = !requireCode || (request.Code?.Trim().Length == 3 && request.Code.Trim().All(char.IsAsciiLetter));
        if (!codeOk || string.IsNullOrWhiteSpace(request.NameAr) || request.ExchangeRate <= 0 || request.DecimalPlaces is < 0 or > 4)
        {
            throw ErpException.Validation(
                "A 3-letter ISO code, a name and a positive exchange rate are required.",
                "رمز العملة (3 أحرف) والاسم وسعر صرف موجب مطلوبة.");
        }
    }

    private static ErpException SettingsNotFound() => ErpException.NotFound("Settings", "الإعدادات");

    private static ErpException CurrencyNotFound() => ErpException.NotFound("Currency", "العملة");
}
