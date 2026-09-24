using System.Globalization;
using Erp.BuildingBlocks.Infrastructure.Modules;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Modules.Settings.Contracts;
using Erp.Modules.Settings.Persistence;
using Erp.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Settings.Application;

internal sealed class SettingsSeeder(SettingsDbContext db, TimeProvider clock) : IModuleSeeder
{
    public int Order => 40;

    public async Task SeedTenantAsync(TenantSeedContext context, CancellationToken cancellationToken)
    {
        if (!await db.TenantSettings.AnyAsync(cancellationToken))
        {
            db.TenantSettings.Add(new TenantSettings(context.TenantId));
        }

        if (!await db.Currencies.AnyAsync(c => c.Code == context.BaseCurrencyCode, cancellationToken))
        {
            db.Currencies.Add(new Currency(context.BaseCurrencyCode, "ريال سعودي", "Saudi Riyal", "ر.س", isBase: true, 1m, 2, clock.GetUtcNow()));
        }
    }
}

internal sealed class NumberSequenceService(SettingsDbContext db, IUnitOfWork unitOfWork, ITenantContext tenant) : INumberSequenceService
{
    private const int DefaultPadding = 4;

    public Task<string> NextAsync(string documentType, DateOnly documentDate, Guid? scopeId, CancellationToken cancellationToken) =>
        NextCoreAsync(documentType, documentDate.Year, scopeId, DefaultPrefix(documentType, documentDate.Year), DefaultPadding, cancellationToken);

    /// <summary>Master-data codes use year 0: one never-resetting series per code type.</summary>
    public Task<string> NextCodeAsync(string codeType, string prefix, int padding, CancellationToken cancellationToken) =>
        NextCoreAsync(codeType, 0, null, prefix, padding, cancellationToken);

    private async Task<string> NextCoreAsync(string documentType, int year, Guid? scopeId, string prefix, int padding, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentType);
        await unitOfWork.EnsureTransactionAsync(cancellationToken);

        var tenantId = tenant.TenantId;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            // Row lock held until the caller's unit of work commits: numbers are gap-free and never shared.
            var rows = await db.Database.SqlQuery<SequenceRow>($"""
                UPDATE [settings].[DocumentSequences] WITH (UPDLOCK, ROWLOCK)
                SET [NextNumber] = [NextNumber] + 1
                OUTPUT deleted.[NextNumber] AS [Number], inserted.[Prefix] AS [Prefix], inserted.[Padding] AS [Padding]
                WHERE [TenantId] = {tenantId} AND [DocumentType] = {documentType} AND [Year] = {year}
                  AND (([ScopeId] IS NULL AND {scopeId} IS NULL) OR [ScopeId] = {scopeId})
                """).ToListAsync(cancellationToken);

            if (rows.Count == 1)
            {
                return Format(rows[0].Prefix, rows[0].Number, rows[0].Padding);
            }

            var sequence = new DocumentSequence(documentType, scopeId, year, prefix, padding);
            db.DocumentSequences.Add(sequence);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return Format(sequence.Prefix, 1, sequence.Padding);
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                // Another transaction created the series first; take a number from it.
                db.Entry(sequence).State = EntityState.Detached;
            }
        }

        throw new InvalidOperationException($"Could not allocate a number for {documentType}.");
    }

    private static string DefaultPrefix(string documentType, int year) =>
        $"{documentType.ToUpperInvariant()}-{year.ToString(CultureInfo.InvariantCulture)}-";

    private static string Format(string prefix, long number, int padding) =>
        prefix + number.ToString(CultureInfo.InvariantCulture).PadLeft(padding, '0');

    private sealed record SequenceRow(long Number, string Prefix, int Padding);
}

internal sealed class CurrencyLookup(SettingsDbContext db) : ICurrencyLookup
{
    public async Task<CurrencyDto?> FindAsync(string code, CancellationToken cancellationToken)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var currency = await db.Currencies.AsNoTracking().SingleOrDefaultAsync(c => c.Code == normalized, cancellationToken);
        return currency is null ? null : ToDto(currency);
    }

    public static CurrencyDto ToDto(Currency c) =>
        new(c.Id, c.Code, c.NameAr, c.NameEn, c.Symbol, c.IsBaseCurrency, c.ExchangeRate, c.DecimalPlaces, c.LastUpdated, c.Status == RecordStatus.Active ? "active" : "inactive");
}
