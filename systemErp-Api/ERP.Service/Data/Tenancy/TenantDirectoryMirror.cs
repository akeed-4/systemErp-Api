using System.Data.Common;
using ERP.Service.Services.Shared;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ERP.Service.Data;

/// <summary>
/// سجلات الدليل لمنشأة مستقلة موجودة في القاعدتين: في المستقلة لأن بياناتها ترتبط بها (المستخدمون، الاشتراك، إعدادات المنشأة)،
/// وفي المشتركة لأن ما يسبق معرفة المنشأة أو يعبر المنشآت يقرؤها منها (الدخول، الدعوة، إشعار الدفع، لوحة المنصة، تفرّد البريد).
/// بعد كل حفظ ناجح (أو تأكيد معاملته) يُنسخ المتغيّر منها إلى القاعدة الأخرى بالمعرّف نفسه. بيانات العمل لا تُنسخ أبداً،
/// ولا إعدادات الهيئة المملوكة للمنشأة (ZatcaConfig): المنشأة تُنسخ بأعمدتها وحدها.
/// </summary>
public sealed class TenantDirectoryMirror : SaveChangesInterceptor, IDbTransactionInterceptor
{
    public static readonly Type[] Types =
    {
        typeof(Tenant), typeof(User), typeof(Subscription), typeof(UserInvite), typeof(OnlinePayment), typeof(PaymentGatewaySettings),
        typeof(TenantWhatsAppIntegration),
    };

    private sealed record Change(Type Type, Guid Id, Guid TenantId, Guid? SourceDedicated, object? Snapshot);

    private readonly ITenantConnectionResolver _resolver;
    private readonly ILogger<TenantDirectoryMirror> _log;
    private readonly Dictionary<DbContext, List<(EntityEntry Entry, bool Deleted)>> _saving = new();
    private readonly Dictionary<DbContext, List<Change>> _pending = new();

    public TenantDirectoryMirror(ITenantConnectionResolver resolver, ILogger<TenantDirectoryMirror> log)
    {
        _resolver = resolver;
        _log = log;
    }

    // ---------- الحفظ: التقاط المتغيّر قبله، ولقطته بعده ----------

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        Capture(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (Snapshot(eventData.Context) is { } db && db.Database.CurrentTransaction == null) FlushAsync(db, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken ct = default)
    {
        if (Snapshot(eventData.Context) is { } db && db.Database.CurrentTransaction == null) await FlushAsync(db, ct);
        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Forget(eventData.Context, saving: true);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken ct = default)
    {
        Forget(eventData.Context, saving: true);
        return Task.CompletedTask;
    }

    private void Capture(DbContext? db)
    {
        if (db is not ErpDbContext { PreserveStamps: false } erp) return;
        var changed = erp.ChangeTracker.Entries<BaseEntity>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && Types.Contains(e.Metadata.ClrType))
            .Select(e => (Entry: (EntityEntry)e, Deleted: e.State == EntityState.Deleted))
            .ToList();
        if (changed.Count > 0) _saving[erp] = changed;
    }

    private DbContext? Snapshot(DbContext? db)
    {
        if (db == null || !_saving.Remove(db, out var saved)) return null;
        // القاعدة التي ذهب إليها الحفظ: المستقلة لمنشأة بعينها، أو المشتركة (سياق Catalog وأي سياق غير موجَّه)
        var source = (db as TenantErpDbContext)?.ConnectedDedicatedTenantId;
        var list = _pending.TryGetValue(db, out var existing) ? existing : _pending[db] = new List<Change>();
        foreach (var (entry, deleted) in saved)
        {
            var entity = (BaseEntity)entry.Entity;
            list.Add(new Change(entry.Metadata.ClrType, entity.Id, entity.TenantId, source, deleted ? null : entry.CurrentValues.ToObject()));
        }
        return db;
    }

    private void Forget(DbContext? db, bool saving)
    {
        if (db == null) return;
        if (saving) _saving.Remove(db);
        else _pending.Remove(db);
    }

    // ---------- المعاملة: لا يُنسخ إلا ما تأكّد ----------

    public DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        Forget(eventData.Context, saving: false);
        return result;
    }

    public ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken ct = default)
    {
        Forget(eventData.Context, saving: false);
        return ValueTask.FromResult(result);
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        if (eventData.Context is { } db) FlushAsync(db, CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken ct = default)
    {
        if (eventData.Context is { } db) await FlushAsync(db, ct);
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => Forget(eventData.Context, saving: false);

    public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken ct = default)
    {
        Forget(eventData.Context, saving: false);
        return Task.CompletedTask;
    }

    // ---------- النسخ ----------

    private async Task FlushAsync(DbContext db, CancellationToken ct)
    {
        if (!_pending.Remove(db, out var changes)) return;
        foreach (var group in changes.GroupBy(c => c.TenantId))
        {
            var tenantId = group.Key;
            try
            {
                var fromDedicated = group.Where(c => c.SourceDedicated == tenantId).ToList();
                var fromCatalog = group.Where(c => c.SourceDedicated == null).ToList();
                if (fromDedicated.Count > 0) await ApplyAsync(_resolver.CatalogConnectionString, tenantId, fromDedicated, ct);
                // المنشأة المشتركة (أو المستقلة قبل جاهزيتها؛ يكمل نسخها مجهِّز القاعدة) لا نسخ لها
                if (fromCatalog.Count > 0 && await _resolver.ReadyDedicatedAsync(tenantId, ct) is { } dedicated)
                    await ApplyAsync(dedicated, tenantId, fromCatalog, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // الحفظ الأصلي تأكّد؛ فشل النسخ يُسجَّل ليُعالَج ولا يُفشل طلب المستخدم بعد نجاحه
                _log.LogError(ex, "تعذّر نسخ سجلات الدليل للمنشأة {TenantId} بين القاعدة المشتركة وقاعدتها المستقلة.", tenantId);
            }
        }
    }

    private async Task ApplyAsync(string connectionString, Guid tenantId, List<Change> changes, CancellationToken ct)
    {
        await using var target = Open(_resolver, connectionString, tenantId);
        foreach (var c in changes) await UpsertAsync(target, c.Type, c.Id, c.Snapshot, ct);
        await target.SaveChangesAsync(ct);
    }

    /// <summary>سياق نسخ على قاعدة بعينها: بلا اعتراضات (لا تدقيق مكرر ولا نسخ عكسي) ويحفظ الأختام كما هي.</summary>
    internal static ErpDbContext Open(ITenantConnectionResolver resolver, string connectionString, Guid tenantId)
    {
        var tenant = new TenantContext();
        tenant.SetTenant(tenantId);
        return new ErpDbContext(resolver.OptionsFor(connectionString), tenant) { PreserveStamps = true };
    }

    /// <summary>إضافة السجل بمعرّفه، أو تحديث أعمدته (لا المملوك منه)، أو حذفه (Snapshot = null).</summary>
    internal static async Task UpsertAsync(ErpDbContext target, Type type, Guid id, object? snapshot, CancellationToken ct)
    {
        var existing = await target.FindAsync(type, new object[] { id }, ct);
        if (snapshot == null)
        {
            if (existing != null) target.Remove(existing);
        }
        else if (existing == null) target.Add(snapshot);
        else target.Entry(existing).CurrentValues.SetValues(snapshot);
    }
}
