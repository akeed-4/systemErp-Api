using ERP.Core.Contracts.Shared;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

public class TransactionRunner : ITransactionRunner
{
    private readonly ErpDbContext _db;
    public TransactionRunner(ErpDbContext db) => _db = db;

    /// <summary>محاولات المعاملة عند تعارض التزامن (رصيد حساب أو صنف غيّرته عملية أخرى في اللحظة نفسها).</summary>
    private const int MaxAttempts = 4;

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
    {
        if (_db.Database.CurrentTransaction != null) return await work(ct); // متداخلة: تنضم للمعاملة الحالية
        for (var attempt = 1; ; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var result = await work(ct);
                await tx.CommitAsync(ct);
                return result;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                // تتراجع المعاملة كلها ثم تُعاد من أولها على الأرصدة المحدَّثة
                _db.ChangeTracker.Clear();
            }
            catch
            {
                _db.ChangeTracker.Clear(); // التراجع تلقائي عند التخلص من معاملة غير مؤكَّدة
                throw;
            }
        }
    }

    public async Task RunAsync(Func<CancellationToken, Task> work, CancellationToken ct = default)
        => await RunAsync<bool>(async t => { await work(t); return true; }, ct);
}
