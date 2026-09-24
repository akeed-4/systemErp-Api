using Erp.Catalog.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace Erp.IntegrationTests.Infrastructure;

/// <summary>
/// One SQL Server for the whole test run: a Testcontainers SQL Server by default, or an existing server when
/// ERP_TEST_SQL_CONNECTION is set (e.g. "Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true").
/// Every database of the run is prefixed with a unique run id and dropped at the end.
/// The catalog and shared databases are migrated once through the real migrator.
/// </summary>
public sealed class ErpTestEnvironment : IAsyncLifetime
{
    public const string ServerOverrideVariable = "ERP_TEST_SQL_CONNECTION";

    private MsSqlContainer? _container;

    public string RunId { get; } = "ErpTest_" + Guid.NewGuid().ToString("N")[..8];

    public string ServerConnectionString { get; private set; } = string.Empty;

    public string CatalogDatabase => $"{RunId}_Catalog";

    public string SharedDatabase => $"{RunId}_Shared";

    public string DedicatedPrefix => $"{RunId}_T_";

    public ErpApiFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable(ServerOverrideVariable);
        if (!string.IsNullOrWhiteSpace(external))
        {
            ServerConnectionString = external;
        }
        else
        {
            _container = new MsSqlBuilder().Build();
            await _container.StartAsync();
            ServerConnectionString = _container.GetConnectionString();
        }

        Factory = new ErpApiFactory(this);
        var report = await Factory.Services.GetRequiredService<IDatabaseMigrationOrchestrator>().RunAsync(CancellationToken.None);
        if (!report.Succeeded)
        {
            throw new InvalidOperationException("Test database migration failed: " + string.Join("; ", report.Databases.Where(d => !d.Succeeded).Select(d => $"{d.Database}: {d.Error}")));
        }
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        SqlConnection.ClearAllPools();
        await DropRunDatabasesAsync();
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public string ConnectionStringFor(string database) =>
        new SqlConnectionStringBuilder(ServerConnectionString) { InitialCatalog = database }.ConnectionString;

    public async Task<int> CountAsync(string database, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionStringFor(database));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task DropDatabaseAsync(string database)
    {
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(ConnectionStringFor("master"));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF DB_ID(@name) IS NOT NULL
            BEGIN
                DECLARE @sql nvarchar(max) = N'ALTER DATABASE ' + QUOTENAME(@name) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + QUOTENAME(@name) + N';';
                EXEC (@sql);
            END
            """;
        command.Parameters.AddWithValue("@name", database);
        await command.ExecuteNonQueryAsync();
    }

    private async Task DropRunDatabasesAsync()
    {
        var names = new List<string>();
        await using (var connection = new SqlConnection(ConnectionStringFor("master")))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sys.databases WHERE name LIKE @prefix";
            command.Parameters.AddWithValue("@prefix", RunId + "%");
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                names.Add(reader.GetString(0));
            }
        }

        foreach (var name in names)
        {
            await DropDatabaseAsync(name);
        }
    }
}

public sealed class ErpApiFactory(ErpTestEnvironment environment) : WebApplicationFactory<Program>
{
    public const string SigningKey = "integration-test-signing-key-0123456789abcdefghij";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Catalog", environment.ConnectionStringFor(environment.CatalogDatabase));
        builder.UseSetting("ConnectionStrings:Shared", environment.ConnectionStringFor(environment.SharedDatabase));
        builder.UseSetting("Tenancy:DedicatedConnectionTemplate", environment.ConnectionStringFor("{database}"));
        builder.UseSetting("Tenancy:DedicatedDatabasePrefix", environment.DedicatedPrefix);
        builder.UseSetting("Tenancy:TenantCacheSeconds", "60");
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("Outbox:Enabled", "false");
        builder.UseSetting("Identity:ExposeOtpInResponse", "true");
        builder.UseSetting("DataProtection:KeysPath", Path.Combine(Path.GetTempPath(), environment.RunId + "_keys"));
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
    }
}

[CollectionDefinition(Name)]
public sealed class ErpCollection : ICollectionFixture<ErpTestEnvironment>
{
    public const string Name = "erp";
}
