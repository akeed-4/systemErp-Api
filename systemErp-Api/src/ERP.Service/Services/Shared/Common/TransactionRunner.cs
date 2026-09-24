using ERP.Core.Contracts.Shared;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

public class TransactionRunner : ITransactionRunner
{
    private readonly ErpDbContext _db;
    public TransactionRunner(ErpDbContext db) => _db = db;

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
    {
        if (_db.Database.CurrentTransaction != null) return await work(ct); // متداخلة: تنضم للمعاملة الحالية
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var result = await work(ct);
            await tx.CommitAsync(ct);
            return result;
        }
        catch
        {
            _db.ChangeTracker.Clear(); // التراجع تلقائي عند التخلص من معاملة غير مؤكَّدة
            throw;
        }
    }

    public async Task RunAsync(Func<CancellationToken, Task> work, CancellationToken ct = default)
        => await RunAsync<bool>(async t => { await work(t); return true; }, ct);
}
