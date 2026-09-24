using Erp.BuildingBlocks.Infrastructure.Options;
using Erp.Catalog.Contracts;
using Erp.SharedKernel.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Erp.BuildingBlocks.Infrastructure.Tenancy;

/// <summary>Routes a tenant to the shared database or to its own dedicated database.</summary>
public interface ITenantConnectionFactory
{
    string SharedConnectionString { get; }

    string GetConnectionString(TenantDescriptor tenant);

    /// <summary>Builds the connection string of a new dedicated database from the configured template.</summary>
    string BuildDedicatedConnectionString(string databaseName);
}

internal sealed class TenantConnectionFactory(IOptions<TenancyOptions> options) : ITenantConnectionFactory
{
    private readonly TenancyOptions _options = options.Value;

    public string SharedConnectionString =>
        string.IsNullOrWhiteSpace(_options.SharedConnectionString)
            ? throw new InvalidOperationException("ConnectionStrings:Shared is not configured.")
            : _options.SharedConnectionString;

    public string GetConnectionString(TenantDescriptor tenant) =>
        tenant.Mode switch
        {
            TenancyMode.Shared => SharedConnectionString,
            TenancyMode.Dedicated => tenant.DedicatedConnectionString
                ?? throw new InvalidOperationException($"Dedicated tenant {tenant.Code} has no connection string in the catalog."),
            _ => throw new InvalidOperationException($"Unknown tenancy mode {tenant.Mode}."),
        };

    public string BuildDedicatedConnectionString(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(_options.DedicatedConnectionTemplate))
        {
            throw new InvalidOperationException("Tenancy:DedicatedConnectionTemplate is not configured.");
        }

        var builder = new SqlConnectionStringBuilder(
            _options.DedicatedConnectionTemplate.Replace("{database}", databaseName, StringComparison.Ordinal))
        {
            InitialCatalog = databaseName,
        };
        return builder.ConnectionString;
    }
}
