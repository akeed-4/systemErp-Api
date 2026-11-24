using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// حسابات النقدية: حسابان رئيسيان تحت الأصول المتداولة، الصناديق (111) والبنوك (110). يُعرف حساب النقدية بسلسلة الآباء الفعلية
/// في الدليل لا ببداية الرمز: حساب تحت 11 برمز 1101 أو 1110 ليس نقدية، وبنك قديم برمز 111xxx نُقل تحت البنوك نقدية.
/// 110 يُعد البنوك فقط إن كان حساب نظام؛ حساب أنشأه المستخدم بالرمز نفسه لا يُمس ولا يُعامل كنقدية، وتبقى البنوك عندها تحت 111.
/// </summary>
internal static class TreasuryAccounts
{
    public sealed record ChartRow(string Code, string? ParentCode, bool IsSystem);

    /// <summary>جذور النقدية من الدليل: <paramref name="isSystemOf"/> يُرجع null للحساب غير الموجود.</summary>
    public static IReadOnlyList<string> Roots(Func<string, bool?> isSystemOf)
    {
        var roots = new List<string>(2);
        if (isSystemOf(DefaultAccounts.CashBoxes) != null) roots.Add(DefaultAccounts.CashBoxes);
        if (isSystemOf(DefaultAccounts.Banks) == true) roots.Add(DefaultAccounts.Banks);
        return roots;
    }

    /// <summary>مجموعة البنوك: 110، أو 111 إن كان الرمز 110 لحساب أنشأه المستخدم.</summary>
    public static async Task<string> BanksGroupAsync(ErpDbContext db, CancellationToken ct)
        => await db.Set<Account>().AnyAsync(a => a.Code == DefaultAccounts.Banks && a.IsSystem, ct) ? DefaultAccounts.Banks : DefaultAccounts.CashBoxes;

    // ---------- ترقيم البنوك والصناديق ----------

    /// <summary>عرض تسلسل الحساب بعد رمز مجموعته: البنوك 110001، 110002...، والصناديق 111001، 111002...</summary>
    public const int ChildWidth = 3;

    /// <summary>هل الأب مجموعة نقدية تُرقَّم فروعها المباشرة آلياً: الصناديق 111، أو البنوك 110 إن كانت حساب نظام (<see cref="Roots"/>).</summary>
    public static async Task<bool> IsNumberedGroupAsync(ErpDbContext db, string? parentCode, CancellationToken ct) => parentCode switch
    {
        DefaultAccounts.CashBoxes => await db.Set<Account>().AnyAsync(a => a.Code == parentCode, ct),
        DefaultAccounts.Banks => await db.Set<Account>().AnyAsync(a => a.Code == parentCode && a.IsSystem, ct),
        _ => false,
    };

    /// <summary>
    /// يصرف رمز الحساب التالي تحت مجموعة النقدية <paramref name="group"/> (المجموعة + 3 خانات) من عدّادها، وهو عدّاد الأب المشترك
    /// لكل الشاشات (<see cref="HierarchicalCodes.AccountChildKey"/>): البنك من شاشة البنوك أو شجرة الحسابات، والصندوق من الشجرة أو
    /// صندوق الموقع، فلا يمس تسلسلُ البنوك تسلسلَ الصناديق. لا يتكرر بين طلبين متزامنين (قفل المعاملة وقفل العدّاد المتفائل)، ولا يعيد
    /// رقماً حُذف حسابه، ويبدأ بعد أكبر رمز قائم بالشكل نفسه. الرموز القديمة بشكل آخر (1111، 1112، 1113) لا تدخل في التسلسل ولا يُعاد
    /// ترقيمها. يُستدعى داخل معاملة إنشاء الحساب.
    /// </summary>
    public static Task<string> NextCodeAsync(ErpDbContext db, string group, CancellationToken ct)
        => HierarchicalCodes.NextAccountChildAsync(db, group, ChildWidth, Messages.SubAccountCodesExhausted, ct);

    /// <summary>الرمز المتوقع التالي تحت المجموعة بلا صرف (للعرض في نوافذ الإضافة)؛ الفعلي يُصرف عند الحفظ.</summary>
    public static Task<string?> PeekCodeAsync(ErpDbContext db, string group, CancellationToken ct)
        => HierarchicalCodes.PeekAccountChildAsync(db, group, ChildWidth, ct);

    public static async Task<Snapshot> LoadAsync(ErpDbContext db, CancellationToken ct)
        => new(await db.Set<Account>().AsNoTracking().Select(a => new ChartRow(a.Code, a.ParentCode, a.IsSystem)).ToListAsync(ct));

    /// <summary>صورة الدليل لتصنيف النقدية.</summary>
    public sealed class Snapshot
    {
        private readonly Dictionary<string, ChartRow> _byCode;
        private readonly HashSet<string> _parents;
        private readonly HashSet<string> _descendants = new(StringComparer.Ordinal);

        public Snapshot(IEnumerable<ChartRow> rows)
        {
            _byCode = rows.GroupBy(r => r.Code, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            _parents = _byCode.Values.Select(r => r.ParentCode).OfType<string>().ToHashSet(StringComparer.Ordinal);
            Roots = TreasuryAccounts.Roots(c => _byCode.TryGetValue(c, out var r) ? r.IsSystem : null);
            foreach (var code in _byCode.Keys)
                if (!Roots.Contains(code) && HasRootAncestor(code)) _descendants.Add(code);
        }

        public IReadOnlyList<string> Roots { get; }

        /// <summary>حساب نقدية: إحدى المجموعتين أو أحد فروعهما (للتقارير: قيد قديم على المجموعة نفسها نقدية أيضاً).</summary>
        public bool IsCash(string code) => _descendants.Contains(code) || Roots.Contains(code);

        /// <summary>حساب نقدية يُرحَّل عليه: فرع بلا أبناء (للتسوية البنكية).</summary>
        public bool IsPostingCash(string code) => _descendants.Contains(code) && !_parents.Contains(code);

        /// <summary>كل رموز النقدية (للاستعلامات: IN بدل StartsWith).</summary>
        public List<string> CashCodes() => _descendants.Concat(Roots).ToList();

        private bool HasRootAncestor(string code)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var c = _byCode[code].ParentCode; c != null && seen.Add(c); c = _byCode.TryGetValue(c, out var p) ? p.ParentCode : null)
                if (Roots.Contains(c)) return true;
            return false;
        }
    }
}
