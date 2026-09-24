using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.BuildingBlocks.Infrastructure.Persistence;

/// <summary>A module's tenant-database context: migrated in <see cref="Order"/> (§17.2) into its own schema.</summary>
public sealed record ModuleDatabase(string ModuleKey, Type ContextType, string Schema, int Order);

public static class ModuleDatabaseServiceCollectionExtensions
{
    /// <summary>
    /// Registers a module DbContext on the scope's shared tenant connection, with its migrations history in its own
    /// schema and the tenant guard interceptor, and lists it for the migrator and the schema version.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services,
        string moduleKey,
        string schema,
        int order)
        where TContext : ModuleDbContext
    {
        services.AddDbContext<TContext>((sp, options) =>
        {
            options.UseSqlServer(
                sp.GetRequiredService<ITenantDbConnection>().Connection,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", schema));
            options.AddInterceptors(sp.GetRequiredService<TenantGuardInterceptor>());
        });

        services.AddSingleton(new ModuleDatabase(moduleKey, typeof(TContext), schema, order));
        return services;
    }
}

/// <summary>Builds design-time options for `dotnet ef migrations add` (no connection is opened).</summary>
public static class DesignTimeOptions
{
    public const string ConnectionString = "Server=(localdb)\\MSSQLLocalDB;Database=ErpDesignTime;Trusted_Connection=True;TrustServerCertificate=True";

    public static DbContextOptions<TContext> For<TContext>(string schema)
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseSqlServer(ConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", schema))
            .Options;
}

/// <summary>Base design-time factory for module contexts.</summary>
public abstract class ModuleDesignTimeFactory<TContext> : IDesignTimeDbContextFactory<TContext>
    where TContext : ModuleDbContext
{
    public TContext CreateDbContext(string[] args) =>
        Create(DesignTimeOptions.For<TContext>(Schema), ModuleDbContextDependencies.DesignTime);

    protected abstract string Schema { get; }

    protected abstract TContext Create(DbContextOptions<TContext> options, ModuleDbContextDependencies dependencies);
}

public static partial class SqlServerDatabases
{
    /// <summary>Creates the database named in <paramref name="connectionString"/> if it does not exist.</summary>
    public static async Task EnsureExistsAsync(string connectionString, CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var database = builder.InitialCatalog;
        if (!SafeDatabaseName().IsMatch(database))
        {
            throw new InvalidOperationException($"Unsafe database name '{database}'.");
        }

        builder.InitialCatalog = "master";
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "IF DB_ID(@name) IS NULL BEGIN DECLARE @sql nvarchar(400) = N'CREATE DATABASE ' + QUOTENAME(@name); EXEC (@sql); END";
        command.Parameters.AddWithValue("@name", database);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static bool IsSafeName(string name) => SafeDatabaseName().IsMatch(name);

    [GeneratedRegex("^[A-Za-z0-9_]{1,120}$")]
    private static partial Regex SafeDatabaseName();
}
