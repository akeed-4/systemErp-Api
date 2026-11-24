using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// قواعد الحساب المرتبط بعميل أو مورد أو بنك، مشتركة بين شاشات الكيانات وشاشة الحسابات:
/// <list type="bullet">
/// <item>الحساب «المملوك» للكيان هو المرتبط به (LinkedEntityType/LinkedEntityId)، أو حساب قديم بلا وسم تحت مجموعة الكيان لا يستخدمه غيره.
/// الحساب العام أو المشترك لا يُحذف ولا يُغيَّر مع الكيان.</item>
/// <item>الحذف والفصل وتغيير الرمز تُرفض إن كان للحساب قيود، أو استخدمه سجل آخر برمزه (أعمدة *AccountCode)،
/// أو أشار إلى الكيان سجل آخر (المفاتيح الأجنبية إليه وأعمدة PartyId). الفحص من نموذج البيانات فيشمل الجداول الجديدة تلقائياً.</item>
/// </list>
/// كل ذلك داخل معاملة المتصل، فلا حذف أو تعديل جزئي عند الرفض.
/// </summary>
internal static class LinkedPartyAccounts
{
    /// <summary>الكيان المرتبط بحساب: نوعه ومعرّفه ورمز حسابه المحفوظ واسمه والسجل نفسه (متتبَّع).</summary>
    public sealed record Party(LinkedEntityType Type, Guid Id, string? AccountCode, string Name, object Entity)
    {
        public string Label => Type switch
        {
            LinkedEntityType.Customer => Messages.PartyCustomer,
            LinkedEntityType.Supplier => Messages.PartySupplier,
            _ => Messages.LabelBank,
        };

        public Type ClrType => Entity.GetType();

        public void SetAccountCode(string code)
        {
            switch (Entity)
            {
                case Customer c: c.AccountCode = code; break;
                case Supplier s: s.AccountCode = code; break;
                case BankEntity b: b.AccountCode = code; break;
            }
        }
    }

    public static Party Of(Customer c) => new(LinkedEntityType.Customer, c.Id, c.AccountCode, c.NameAr, c);
    public static Party Of(Supplier s) => new(LinkedEntityType.Supplier, s.Id, s.AccountCode, s.NameAr, s);
    public static Party Of(BankEntity b) => new(LinkedEntityType.Bank, b.Id, b.AccountCode, b.NameAr, b);

    public static bool IsParty(LinkedEntityType? type) => type is LinkedEntityType.Customer or LinkedEntityType.Supplier or LinkedEntityType.Bank;

    /// <summary>مجموعة حسابات الكيان في الدليل (العملاء 112، الموردون 211، البنوك 111).</summary>
    public static string GroupOf(LinkedEntityType type) => type switch
    {
        LinkedEntityType.Customer => DefaultAccounts.Receivables,
        LinkedEntityType.Supplier => DefaultAccounts.Payables,
        LinkedEntityType.Bank => DefaultAccounts.Banks,
        _ => throw new ValidationFailedException(Messages.EntityTypeNotSupportedForAutoAccount),
    };

    public static async Task<Party?> FindAsync(ErpDbContext db, LinkedEntityType type, Guid id, CancellationToken ct) => type switch
    {
        LinkedEntityType.Customer => await db.Set<Customer>().FirstOrDefaultAsync(c => c.Id == id, ct) is { } c ? Of(c) : null,
        LinkedEntityType.Supplier => await db.Set<Supplier>().FirstOrDefaultAsync(s => s.Id == id, ct) is { } s ? Of(s) : null,
        LinkedEntityType.Bank => await db.Set<BankEntity>().FirstOrDefaultAsync(b => b.Id == id, ct) is { } b ? Of(b) : null,
        _ => null,
    };

    /// <summary>
    /// مالك الحساب (برمزه <paramref name="code"/>): الكيان المرتبط به، أو لحساب قديم بلا وسم تحت مجموعة كيان، الكيانُ الوحيد الذي يحمل رمزه.
    /// حساب النظام والرئيسي وما يحمل رمزَه أكثر من كيان: بلا مالك (مشترك).
    /// </summary>
    public static async Task<Party?> OwnerOfAsync(ErpDbContext db, Account account, string code, CancellationToken ct)
    {
        if (IsParty(account.LinkedEntityType) && account.LinkedEntityId is Guid id)
            return await FindAsync(db, account.LinkedEntityType!.Value, id, ct);
        if (account.LinkedEntityType is not (null or LinkedEntityType.General) || account.IsSystem || account.IsGroup) return null;

        var users = new List<Party>();
        users.AddRange((await db.Set<Customer>().Where(c => c.AccountCode == code).ToListAsync(ct)).Select(Of));
        users.AddRange((await db.Set<Supplier>().Where(s => s.AccountCode == code).ToListAsync(ct)).Select(Of));
        users.AddRange((await db.Set<BankEntity>().Where(b => b.AccountCode == code).ToListAsync(ct)).Select(Of));
        if (users.Count != 1) return null;
        var group = GroupOf(users[0].Type);
        return code.Length > group.Length && code.StartsWith(group, StringComparison.Ordinal) ? users[0] : null;
    }

    /// <summary>الحساب المملوك للكيان، أو null إن كان رمزه المحفوظ لحساب عام/مشترك (فلا يُمس).</summary>
    public static async Task<Account?> OwnedAccountAsync(ErpDbContext db, Party party, CancellationToken ct)
    {
        var linked = await db.Set<Account>().FirstOrDefaultAsync(a => a.LinkedEntityType == party.Type && a.LinkedEntityId == party.Id, ct);
        if (linked != null) return linked;
        if (string.IsNullOrWhiteSpace(party.AccountCode)) return null;
        var legacy = await db.Set<Account>().FirstOrDefaultAsync(a => a.Code == party.AccountCode, ct);
        return legacy != null && (await OwnerOfAsync(db, legacy, legacy.Code, ct))?.Id == party.Id ? legacy : null;
    }

    /// <summary>
    /// الحساب حرّ للحذف أو تغيير الهوية: بلا قيود (<paramref name="entriesMessage"/>)، وبلا حسابات فرعية، ولا يستخدم رمزَه سجلٌّ آخر
    /// (مستند، إعداد، طريقة دفع...) غير الكيان المالك نفسه.
    /// </summary>
    public static async Task EnsureAccountFreeAsync(ErpDbContext db, Account account, Party? owner, string entriesMessage, CancellationToken ct)
    {
        if (await db.Set<JournalEntryLine>().AnyAsync(l => l.AccountCode == account.Code, ct)) throw new ConflictException(entriesMessage);
        if (await db.Set<Account>().AnyAsync(a => a.ParentCode == account.Code, ct)) throw new ConflictException(Messages.CannotDeleteAccountWithChildren);
        var users = await CodeReferencesAsync(db, account, owner, ct);
        if (users.Count > 0) throw new ConflictException(string.Format(Messages.LinkedAccountInUse, account.Code, string.Join("، ", users)));
    }

    /// <summary>لا يشير إلى الكيان سجلٌّ آخر (فاتورة، عرض سعر، أمر، عقد، سند...): وإلا يُرفض حذفه مع ذكر ما يمنعه.</summary>
    public static async Task EnsurePartyUnreferencedAsync(ErpDbContext db, Party party, CancellationToken ct)
    {
        var users = await PartyReferencesAsync(db, party, ct);
        if (users.Count > 0) throw new ConflictException(string.Format(Messages.PartyHasLinkedRecords, party.Label, party.Name, string.Join("، ", users)));
    }

    /// <summary>
    /// فحص حذف الكيان كاملاً، ويُرجع حسابه المملوك ليُحذف معه (null للحساب العام/المشترك: يبقى كما هو).
    /// </summary>
    public static async Task<Account?> PrepareDeleteAsync(ErpDbContext db, Party party, string entriesMessage, CancellationToken ct)
    {
        var account = await OwnedAccountAsync(db, party, ct);
        if (account != null) await EnsureAccountFreeAsync(db, account, party, entriesMessage, ct);
        await EnsurePartyUnreferencedAsync(db, party, ct);
        return account;
    }

    // ---------- فحص المراجع من نموذج البيانات ----------

    /// <summary>جداول تشير إلى الكيان: مفاتيحها الأجنبية إليه وأعمدة PartyId (بلا مفتاح).</summary>
    private static async Task<List<string>> PartyReferencesAsync(ErpDbContext db, Party party, CancellationToken ct)
    {
        var entityType = db.Model.FindEntityType(party.ClrType) ?? throw new InvalidOperationException(party.ClrType.Name);
        var columns = entityType.GetReferencingForeignKeys().Where(fk => fk.Properties.Count == 1)
            .Select(fk => (Owner: fk.DeclaringEntityType, Property: fk.Properties[0]))
            .Concat(db.Model.GetEntityTypes().SelectMany(t => t.GetDeclaredProperties()
                .Where(p => p.Name == "PartyId" && (p.ClrType == typeof(Guid) || p.ClrType == typeof(Guid?)))
                .Select(p => (Owner: t, Property: p))));

        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (owner, property) in columns)
            foreach (var (table, column, _, _) in Columns(property))
                if (await ExistsAsync(db, $"{table} WHERE {column} = {{0}}", new object[] { party.Id }, ct))
                    found.Add(owner.ClrType.Name);
        return found.ToList();
    }

    /// <summary>
    /// سجلات تحمل رمز الحساب نصاً (أعمدة *AccountCode) في المنشأة نفسها، عدا الدليل نفسه وأسطر القيود (تُفحص منفصلة)
    /// وصفّ الكيان المالك.
    /// </summary>
    private static async Task<List<string>> CodeReferencesAsync(ErpDbContext db, Account account, Party? owner, CancellationToken ct)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in db.Model.GetEntityTypes().Where(t => t.ClrType != typeof(Account) && t.ClrType != typeof(JournalEntryLine)))
            foreach (var property in type.GetDeclaredProperties().Where(p => p.ClrType == typeof(string) && p.Name.EndsWith("AccountCode", StringComparison.Ordinal)))
                foreach (var (table, column, tenant, id) in Columns(property))
                {
                    var where = $"{table} WHERE {column} = {{0}}" + (tenant == null ? "" : $" AND {tenant} = {{1}}");
                    var args = new List<object> { account.Code, account.TenantId };
                    if (owner != null && owner.ClrType == type.ClrType && id != null) { where += $" AND {id} <> {{2}}"; args.Add(owner.Id); }
                    if (await ExistsAsync(db, where, args.ToArray(), ct)) found.Add(type.ClrType.Name);
                }
        return found.ToList();
    }

    /// <summary>جدول العمود وعموده (ومعهما عمودا المنشأة والمعرّف في الجدول نفسه إن وُجدا)، بأسماء محاطة بأقواس.</summary>
    private static IEnumerable<(string Table, string Column, string? Tenant, string? Id)> Columns(IProperty property)
    {
        foreach (var mapping in property.GetTableColumnMappings())
        {
            var table = mapping.Column.Table;
            var name = table.Schema == null ? $"[{table.Name}]" : $"[{table.Schema}].[{table.Name}]";
            var tenant = table.FindColumn(nameof(BaseEntity.TenantId));
            var id = table.FindColumn(nameof(BaseEntity.Id));
            yield return (name, $"[{mapping.Column.Name}]", tenant == null ? null : $"[{tenant.Name}]", id == null ? null : $"[{id.Name}]");
        }
    }

    private static async Task<bool> ExistsAsync(ErpDbContext db, string fromWhere, object[] args, CancellationToken ct)
        => (await db.Database.SqlQueryRaw<int>($"SELECT TOP(1) 1 AS [Value] FROM {fromWhere}", args).ToListAsync(ct)).Count > 0;
}
