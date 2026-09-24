namespace Erp.Modules.Settings.Contracts;

/// <summary>
/// Gap-free, per-tenant document numbers (e.g. INV-2026-0001). Takes a row lock inside the caller's unit of work,
/// so the number is only consumed if the document commits. Scope = terminal/branch for per-device series.
/// </summary>
public interface INumberSequenceService
{
    Task<string> NextAsync(string documentType, DateOnly documentDate, Guid? scopeId, CancellationToken cancellationToken);

    /// <summary>A yearless series for master-data codes, e.g. NextCodeAsync("customer", "CUST-", 4) → CUST-0001.</summary>
    Task<string> NextCodeAsync(string codeType, string prefix, int padding, CancellationToken cancellationToken);
}

public sealed record CurrencyDto(
    Guid Id,
    string Code,
    string NameAr,
    string NameEn,
    string Symbol,
    bool IsBaseCurrency,
    decimal ExchangeRate,
    int DecimalPlaces,
    DateTimeOffset LastUpdated,
    string Status);

public interface ICurrencyLookup
{
    Task<CurrencyDto?> FindAsync(string code, CancellationToken cancellationToken);
}
