using System.Data.Common;
using Erp.SharedKernel.Tenancy;
using Microsoft.Data.SqlClient;

namespace Erp.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// The single DbConnection shared by every module DbContext in a scope. It is created lazily on the current tenant's
/// database (or on an explicit database for platform work) and stays bound to that tenant for the scope's lifetime.
/// </summary>
public interface ITenantDbConnection
{
    DbConnection Connection { get; }

    /// <summary>Targets an explicit database (platform scopes: migrator, outbox). Must be called before first use.</summary>
    void UseDatabase(string connectionString);
}

internal sealed class TenantDbConnection(ITenantContext tenant) : ITenantDbConnection, IAsyncDisposable, IDisposable
{
    private SqlConnection? _connection;
    private string? _explicitConnectionString;
    private Guid? _boundTenantId;

    public DbConnection Connection
    {
        get
        {
            if (_connection is null)
            {
                string connectionString;
                if (_explicitConnectionString is not null)
                {
                    connectionString = _explicitConnectionString;
                }
                else
                {
                    if (!tenant.IsResolved)
                    {
                        throw new TenantNotResolvedException();
                    }

                    connectionString = tenant.ConnectionString;
                    _boundTenantId = tenant.TenantId;
                }

                _connection = new SqlConnection(connectionString);
            }
            else if (_boundTenantId is { } bound && tenant.IsResolved && tenant.TenantId != bound)
            {
                throw new TenantIsolationViolationException(
                    "The database connection of this scope is bound to another tenant.");
            }

            return _connection;
        }
    }

    public void UseDatabase(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        if (_connection is not null)
        {
            throw new InvalidOperationException("The connection is already in use; choose the database before first use.");
        }

        _explicitConnectionString = connectionString;
    }

    public void Dispose() => _connection?.Dispose();

    public ValueTask DisposeAsync() => _connection?.DisposeAsync() ?? ValueTask.CompletedTask;
}
