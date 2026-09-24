using System.Diagnostics;
using Erp.BuildingBlocks.Infrastructure.Schema;
using Erp.BuildingBlocks.Infrastructure.Options;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.BuildingBlocks.Infrastructure.Security;
using Erp.BuildingBlocks.Infrastructure.Tenancy;
using Erp.Catalog.Contracts;
using Erp.Catalog.Persistence;
using Erp.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Erp.Catalog.Services;

public sealed record DatabaseMigrationOutcome(
    string Database,
    string Kind,
    bool Succeeded,
    int AppliedMigrations,
    string? Error,
    TimeSpan Duration);

public sealed record MigrationReport(string SchemaVersion, IReadOnlyList<DatabaseMigrationOutcome> Databases)
{
    public bool Succeeded => Databases.All(d => d.Succeeded);
}

/// <summary>
/// What Erp.Migrator runs: catalog → shared DB → every dedicated DB listed in the catalog.
/// Idempotent and resumable; one failing dedicated database does not stop the others.
/// Updates catalog.Tenants.SchemaVersion for every tenant whose database was brought up to date.
/// </summary>
public interface IDatabaseMigrationOrchestrator
{
    Task<MigrationReport> RunAsync(CancellationToken cancellationToken);
}

internal sealed class DatabaseMigrationOrchestrator(
    IDbContextFactory<CatalogDbContext> catalogFactory,
    TenantDatabaseMigrator tenantDatabases,
    ITenantConnectionFactory connections,
    IConnectionStringProtector protector,
    ITenantDirectory directory,
    IOptions<TenancyOptions> options,
    ISchemaVersionProvider schemaVersions,
    ILogger<DatabaseMigrationOrchestrator> logger) : IDatabaseMigrationOrchestrator
{
    public async Task<MigrationReport> RunAsync(CancellationToken cancellationToken)
    {
        var outcomes = new List<DatabaseMigrationOutcome>();

        var catalog = await RunStepAsync(DatabaseName(options.Value.CatalogConnectionString), "catalog", MigrateCatalogAsync, cancellationToken);
        outcomes.Add(catalog);
        if (!catalog.Succeeded)
        {
            // Without the catalog there is no tenant list; stop here.
            return new MigrationReport(schemaVersions.ExpectedVersion, outcomes);
        }

        outcomes.Add(await RunStepAsync(
            DatabaseName(connections.SharedConnectionString),
            "shared",
            async ct =>
            {
                var result = await tenantDatabases.MigrateAsync(connections.SharedConnectionString, ct);
                await StampSharedTenantsAsync(result.SchemaVersion, ct);
                return result.AppliedMigrations;
            },
            cancellationToken));

        List<CatalogTenant> dedicated;
        await using (var db = await catalogFactory.CreateDbContextAsync(cancellationToken))
        {
            dedicated = await db.Tenants.AsNoTracking()
                .Where(t => t.TenancyMode == TenancyMode.Dedicated && t.Status != TenantStatus.Archived && t.ConnectionStringEncrypted != null)
                .OrderBy(t => t.Code)
                .ToListAsync(cancellationToken);
        }

        foreach (var tenant in dedicated)
        {
            outcomes.Add(await RunStepAsync(
                tenant.DatabaseName ?? tenant.Code,
                $"dedicated ({tenant.Code})",
                async ct =>
                {
                    var result = await tenantDatabases.MigrateAsync(protector.Unprotect(tenant.ConnectionStringEncrypted!), ct);
                    await StampTenantAsync(tenant.Id, result.SchemaVersion, ct);
                    return result.AppliedMigrations;
                },
                cancellationToken));
        }

        return new MigrationReport(schemaVersions.ExpectedVersion, outcomes);
    }

    /// <summary>Creates/migrates the catalog database and upserts plans, roles and screens.</summary>
    public async Task<int> MigrateCatalogAsync(CancellationToken cancellationToken)
    {
        await SqlServerDatabases.EnsureExistsAsync(options.Value.CatalogConnectionString, cancellationToken);
        await using var db = await catalogFactory.CreateDbContextAsync(cancellationToken);
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).Count();
        await db.Database.MigrateAsync(cancellationToken);
        await CatalogSeedData.UpsertAsync(db, cancellationToken);
        return pending;
    }

    private async Task<DatabaseMigrationOutcome> RunStepAsync(
        string database,
        string kind,
        Func<CancellationToken, Task<int>> step,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var applied = await step(cancellationToken);
            logger.LogInformation("Migrated {Kind} database {Database}: {Applied} migration(s) applied", kind, database, applied);
            return new DatabaseMigrationOutcome(database, kind, true, applied, null, stopwatch.Elapsed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Migration failed for {Kind} database {Database}", kind, database);
            return new DatabaseMigrationOutcome(database, kind, false, 0, ex.Message, stopwatch.Elapsed);
        }
    }

    private async Task StampSharedTenantsAsync(string version, CancellationToken cancellationToken)
    {
        await using var db = await catalogFactory.CreateDbContextAsync(cancellationToken);
        var ids = await db.Tenants.Where(t => t.TenancyMode == TenancyMode.Shared).Select(t => t.Id).ToListAsync(cancellationToken);
        await db.Tenants.Where(t => t.TenancyMode == TenancyMode.Shared)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.SchemaVersion, version), cancellationToken);
        ids.ForEach(directory.Invalidate);
    }

    private async Task StampTenantAsync(Guid tenantId, string version, CancellationToken cancellationToken)
    {
        await using var db = await catalogFactory.CreateDbContextAsync(cancellationToken);
        await db.Tenants.Where(t => t.Id == tenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.SchemaVersion, version), cancellationToken);
        directory.Invalidate(tenantId);
    }

    private static string DatabaseName(string connectionString) =>
        new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString).InitialCatalog;
}
