using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace ERP.Service.Data;

/// <summary>
/// حلقة مفاتيح Data Protection (تشفير أسرار بوابات الدفع وسلاسل اتصال القواعد المستقلة) في جدول
/// <c>DataProtectionKeys</c> بالقاعدة المشتركة دائماً، لا في مجلد على السرفر: تبقى بعد إعادة النشر أو نقل السرفر،
/// وتتشاركها كل نسخ الخادم ما دامت على القاعدة نفسها. الجدول يُنشأ بمهاجرات <see cref="ErpDbContext"/>،
/// فهذا السياق مستثنى منها (لا مهاجرات له).
/// </summary>
public sealed class DataProtectionKeysDbContext : DbContext, IDataProtectionKeyContext
{
    public const string TableName = "DataProtectionKeys";

    public DataProtectionKeysDbContext(DbContextOptions<DataProtectionKeysDbContext> options) : base(options) { }

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => Configure(modelBuilder, excludeFromMigrations: true);

    /// <summary>تعريف الجدول المشترك بين هذا السياق ومهاجرات <see cref="ErpDbContext"/>.</summary>
    internal static void Configure(ModelBuilder modelBuilder, bool excludeFromMigrations)
        => modelBuilder.Entity<DataProtectionKey>(e =>
        {
            e.ToTable(TableName, t => { if (excludeFromMigrations) t.ExcludeFromMigrations(); });
            e.HasKey(k => k.Id);
            e.Property(k => k.FriendlyName).HasMaxLength(256);
            e.HasIndex(k => k.FriendlyName);
        });

    /// <summary>
    /// ينقل مفاتيح المجلد القديم (DataProtection:KeysPath) إلى الجدول مرة واحدة: كل مفتاح غير موجود في الجدول يُضاف،
    /// فتبقى الأسرار المشفّرة سابقاً قابلة للفك بعد الانتقال إلى التخزين في القاعدة. يُرجع عدد المفاتيح المضافة.
    /// </summary>
    public async Task<int> ImportKeysFromFolderAsync(string? folder, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return 0;
        var existing = (await DataProtectionKeys.Select(k => k.FriendlyName).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var file in Directory.GetFiles(folder, "key-*.xml"))
        {
            // اسم المستودع للمفتاح كما يحفظه FileSystemXmlRepository وEntityFrameworkCoreXmlRepository: key-<المعرّف>
            var name = Path.GetFileNameWithoutExtension(file);
            if (existing.Contains(name)) continue;
            var xml = XElement.Load(file).ToString(SaveOptions.DisableFormatting);
            DataProtectionKeys.Add(new DataProtectionKey { FriendlyName = name, Xml = xml });
            existing.Add(name);
            added++;
        }
        if (added > 0) await SaveChangesAsync(ct);
        return added;
    }
}
