using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Erp.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// One transaction over the scope's single connection, shared by every module DbContext resolved in the scope.
/// Always save through this, never through a DbContext directly, so all modules commit or roll back together.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>Runs <paramref name="work"/> in the ambient transaction, then saves every context and commits (outermost call only).</summary>
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default);

    Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default);

    /// <summary>Saves every enlisted context; commits unless called inside <see cref="ExecuteAsync{T}"/>.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Begins the ambient transaction if none is open (e.g. before a locking number-sequence update).</summary>
    Task EnsureTransactionAsync(CancellationToken cancellationToken = default);

    void Enlist(DbContext context);
}

internal sealed class UnitOfWork(ITenantDbConnection connection, IEnumerable<ModuleDatabase> modules) : IUnitOfWork, IAsyncDisposable
{
    // Contexts are flushed in migration order so principals (accounts, banks…) are written before the rows that reference
    // them through cross-module foreign keys.
    private readonly Dictionary<Type, int> _flushOrder = modules.ToDictionary(m => m.ContextType, m => m.Order);
    private readonly List<DbContext> _contexts = [];
    private DbTransaction? _transaction;
    private int _depth;

    public void Enlist(DbContext context)
    {
        _contexts.Add(context);
        if (_transaction is not null)
        {
            context.Database.UseTransaction(_transaction);
        }
    }

    public async Task EnsureTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            return;
        }

        var dbConnection = connection.Connection;
        if (dbConnection.State != ConnectionState.Open)
        {
            await dbConnection.OpenAsync(cancellationToken);
        }

        _transaction = await dbConnection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        foreach (var context in _contexts)
        {
            await context.Database.UseTransactionAsync(_transaction, cancellationToken);
        }
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureTransactionAsync(cancellationToken);
        if (_depth > 0)
        {
            await FlushAsync(cancellationToken);
            return;
        }

        try
        {
            await FlushAsync(cancellationToken);
            await CommitAsync(cancellationToken);
        }
        catch
        {
            await RollbackAsync();
            throw;
        }
    }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default)
    {
        await EnsureTransactionAsync(cancellationToken);
        _depth++;
        try
        {
            var result = await work(cancellationToken);
            await FlushAsync(cancellationToken);
            _depth--;
            if (_depth == 0)
            {
                await CommitAsync(cancellationToken);
            }

            return result;
        }
        catch
        {
            _depth = Math.Max(0, _depth - 1);
            if (_depth == 0)
            {
                await RollbackAsync();
            }

            throw;
        }
    }

    public Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default) =>
        ExecuteAsync<bool>(
            async ct =>
            {
                await work(ct);
                return true;
            },
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        foreach (var context in _contexts.OrderBy(c => _flushOrder.GetValueOrDefault(c.GetType(), int.MaxValue)).ToList())
        {
            if (context.ChangeTracker.HasChanges())
            {
                await context.SaveChangesAsync(cancellationToken);
            }
        }
    }

    private async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (_transaction is null)
        {
            return;
        }

        await _transaction.CommitAsync(cancellationToken);
        await ReleaseTransactionAsync();
    }

    private async Task RollbackAsync()
    {
        if (_transaction is null)
        {
            return;
        }

        try
        {
            await _transaction.RollbackAsync();
        }
        finally
        {
            await ReleaseTransactionAsync();
            foreach (var context in _contexts)
            {
                context.ChangeTracker.Clear();
            }
        }
    }

    private async Task ReleaseTransactionAsync()
    {
        var transaction = _transaction;
        _transaction = null;
        foreach (var context in _contexts)
        {
            await context.Database.UseTransactionAsync(null);
        }

        if (transaction is not null)
        {
            await transaction.DisposeAsync();
        }
    }
}
