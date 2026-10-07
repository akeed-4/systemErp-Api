using ERP.Core.Contracts.Shared;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ERP.Service.Data;

/// <summary>
/// سجل تدقيق تلقائي: كل إنشاء/تعديل/حذف لمستند أو بيان أساسي يُسجَّل معه من قام به وما الذي تغيّر (القيمة قبل ← بعد)،
/// في المعاملة نفسها ومن مكان واحد، دون أن تتذكّر كل خدمة أن تسجّل. الأحداث ذات المعنى (ترحيل، عكس، إقفال)
/// تبقى تُسجَّل صراحةً من خدماتها عبر IAuditService.
/// </summary>
public class AuditTrailInterceptor : SaveChangesInterceptor
{
    // المستندات والبيانات الأساسية والإعدادات. القيود والسندات وحركات المخزون تُسجَّل من خدماتها (الآلي منها كثير وليس فعل مستخدم).
    private static readonly HashSet<Type> Audited = new()
    {
        typeof(Invoice), typeof(Quotation), typeof(CommercialOrder), typeof(DeliveryNote), typeof(CommercialContract), typeof(Agreement),
        typeof(Product), typeof(ProductCategory), typeof(UnitOfMeasure), typeof(Customer), typeof(Supplier), typeof(BankEntity),
        typeof(Currency), typeof(PaymentMethodItem), typeof(Warehouse), typeof(Account), typeof(CostCenter), typeof(FixedAsset), typeof(Employee),
        typeof(User), typeof(UserRolePermission), typeof(Tenant), typeof(ZatcaConfig), typeof(CostingPolicy), typeof(PosInvoiceSettings),
    };

    // حقول يحدّثها النظام مع كل حركة (أرصدة، كميات، تكلفة، طوابع): تغيّرها وحده ليس تعديلاً من مستخدم
    private static readonly HashSet<string> SystemFields = new()
    {
        nameof(BaseEntity.UpdatedAt), nameof(BaseEntity.CreatedAt), nameof(BaseEntity.TenantId), "RowVersion", "Version",
        nameof(Product.CurrentStock), nameof(Product.AverageCost), nameof(Product.LastPurchaseCost), "LastLoginAt",
    };

    private static readonly string[] Secrets = { "Password", "Secret", "Token", "Hash", "Key", "Otp" };
    private static readonly string[] LabelProperties = { "InvoiceNumber", "QuotationNumber", "OrderNumber", "DeliveryNumber", "ContractNumber", "AgreementNumber", "Code", "Sku", "Email", "NameAr", "Name" };

    private const int MaxFields = 12;
    private const int MaxValueLength = 80;

    private readonly ICurrentUser _user;
    public AuditTrailInterceptor(ICurrentUser user) => _user = user;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        Record(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Record(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void Record(DbContext? db)
    {
        if (db == null) return;
        var now = DateTime.UtcNow;
        var logs = new List<AuditLog>();
        foreach (var entry in db.ChangeTracker.Entries<BaseEntity>().ToList())
        {
            if (!Audited.Contains(entry.Entity.GetType())) continue;
            var details = entry.State switch
            {
                EntityState.Added => Label(entry),
                EntityState.Deleted => Label(entry),
                EntityState.Modified => Changes(entry),
                _ => null,
            };
            if (details == null) continue;
            logs.Add(new AuditLog
            {
                TenantId = entry.Entity.TenantId, CreatedAt = now, Timestamp = now, PerformedBy = _user.Name ?? "system",
                Action = entry.State switch { EntityState.Added => "create", EntityState.Deleted => "delete", _ => "update" },
                EntityName = entry.Entity.GetType().Name, EntityId = entry.Entity.Id.ToString(), Details = details,
            });
        }
        if (logs.Count > 0) db.AddRange(logs);
    }

    private static bool IsSystemField(string name) => SystemFields.Contains(name) || name.EndsWith("Balance", StringComparison.Ordinal);

    /// <summary>الحقول التي غيّرها المستخدم «قبل ← بعد»؛ null إن لم يتغيّر إلا ما يحدّثه النظام.</summary>
    private static string? Changes(EntityEntry entry)
    {
        var changed = entry.Properties
            .Where(p => p.IsModified && !IsSystemField(p.Metadata.Name) && !Equals(p.OriginalValue, p.CurrentValue)).ToList();
        if (changed.Count == 0) return null;
        var parts = changed.Take(MaxFields).Select(p => IsSecret(p.Metadata.Name)
            ? $"{p.Metadata.Name}: ***"
            : $"{p.Metadata.Name}: {Text(p.OriginalValue)} ← {Text(p.CurrentValue)}");
        var label = Label(entry);
        return $"{label} | " + string.Join("؛ ", parts) + (changed.Count > MaxFields ? $"؛ (+{changed.Count - MaxFields})" : string.Empty);
    }

    private static string Label(EntityEntry entry)
    {
        foreach (var name in LabelProperties)
        {
            var property = entry.Properties.FirstOrDefault(p => p.Metadata.Name == name);
            var value = entry.State == EntityState.Deleted ? property?.OriginalValue : property?.CurrentValue;
            if (value is string text && !string.IsNullOrWhiteSpace(text)) return text;
        }
        return entry.Entity.GetType().Name;
    }

    private static bool IsSecret(string name) => Secrets.Any(s => name.Contains(s, StringComparison.OrdinalIgnoreCase));

    private static string Text(object? value)
    {
        var text = value switch
        {
            null => "—",
            DateTime date => date.ToString("yyyy-MM-dd HH:mm"),
            decimal number => number.ToString("0.####"),
            _ => value.ToString() ?? string.Empty,
        };
        return text.Length > MaxValueLength ? text[..MaxValueLength] + "…" : text;
    }
}
