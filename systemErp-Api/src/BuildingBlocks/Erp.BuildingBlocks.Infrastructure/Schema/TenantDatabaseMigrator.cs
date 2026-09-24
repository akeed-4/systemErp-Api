using System.Reflection;
using Erp.BuildingBlocks.Infrastructure.Modules;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.BuildingBlocks.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Erp.BuildingBlocks.Infrastructure.Schema;

/// <summary>
/// The schema version the code expects in every tenant database:
/// "platform=&lt;last migration&gt;;org=…;identity=…" in migration order.
/// Written to catalog.Tenants.SchemaVersion by the migrator; the API refuses tenants whose value differs.
/// </summary>
public interface ISchemaVersionProvider
{
    string ExpectedVersion { get; }
}

internal sealed class SchemaVersionProvider(IEnumerable<ModuleDatabase> modules) : ISchemaVersionProvider
{
    private readonly Lazy<string> _version = new(() => Compute(modules));

    public string ExpectedVersion => _version.Value;

    private static string Compute(IEnumerable<ModuleDatabase> modules)
    {
        var parts = new List<string>();
        foreach (var module in modules.OrderBy(m => m.Order))
        {
            using var context = CreateProbe(module);
            parts.Add($"{module.ModuleKey}={context.Database.GetMigrations().LastOrDefault() ?? "none"}");
        }

        return string.Join(';', parts);
    }

    // Migration discovery reads the assembly only; the dummy connection is never opened.
    private static DbContext CreateProbe(ModuleDatabase module)
    {
        var builderType = typeof(DbContextOptionsBuilder<>).MakeGenericType(module.ContextType);
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(builderType)!;
        builder.UseSqlServer(DesignTimeOptions.ConnectionString);
        return (DbContext)Activator.CreateInstance(
            module.ContextType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [builder.Options, ModuleDbContextDependencies.DesignTime],
            culture: null)!;
    }
}

public sealed record TenantDatabaseMigrationResult(string SchemaVersion, int AppliedMigrations);

/// <summary>
/// Brings one tenant database (shared or dedicated) to the current schema: create if missing, apply every module's
/// migrations in order, then copy the global reference data. Idempotent.
/// </summary>
public sealed class TenantDatabaseMigrator(
    ITenantScopeFactory scopes,
    IEnumerable<ModuleDatabase> modules,
    IGlobalReferenceDataSource referenceData,
    ISchemaVersionProvider schemaVersions,
    ILogger<TenantDatabaseMigrator> logger)
{
    public async Task<TenantDatabaseMigrationResult> MigrateAsync(string connectionString, CancellationToken cancellationToken)
    {
        await SqlServerDatabases.EnsureExistsAsync(connectionString, cancellationToken);

        var applied = 0;
        await using var scope = scopes.CreateForDatabase(connectionString, "schema migration");
        foreach (var module in modules.OrderBy(m => m.Order))
        {
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(module.ContextType);
            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            if (pending.Count > 0)
            {
                logger.LogInformation("Applying {Count} migration(s) for module {Module}", pending.Count, module.ModuleKey);
                await context.Database.MigrateAsync(cancellationToken);
                applied += pending.Count;
            }
        }

        var data = await referenceData.GetAsync(cancellationToken);
        foreach (var seeder in scope.ServiceProvider.GetServices<IReferenceDataSeeder>())
        {
            await seeder.SeedAsync(data, cancellationToken);
        }

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
        return new TenantDatabaseMigrationResult(schemaVersions.ExpectedVersion, applied);
    }

    /// <summary>True when every module's migrations are applied to the database (used before seeding into the shared DB).</summary>
    public async Task<bool> IsUpToDateAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateForDatabase(connectionString, "schema check");
        foreach (var module in modules)
        {
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(module.ContextType);
            if (!await context.Database.CanConnectAsync(cancellationToken)
                || (await context.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            {
                return false;
            }
        }

        return true;
    }
}
