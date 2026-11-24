using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// حسابات النقدية: حسابان رئيسيان تحت الأصول المتداولة، الصناديق (111) والبنوك (110). يُعرف حساب النقدية بسلسلة الآباء الفعلية
/// في الدليل لا ببداية الرمز: حساب تحت 11 برمز يبدأ بـ 110 أو 111 ليس نقدية، وبنك قديم برمز 111xxx نُقل تحت البنوك نقدية.
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
    //
    // تسلسل واحد لكل مجموعة يشمل الحسابات الافتراضية والمضافة: رمز المجموعة + رقم التسلسل بلا أصفار بادئة.
    //   البنوك 110:    1101 (البنك الافتراضي)، 1102 (تحصيلات البوابة)، 1103 أول بنك مضاف، ... 1109، 11010، 11011...
    //   الصناديق 111:  1111 (الصندوق الرئيسي)، 1112 أول صندوق إضافي، ... 1119، 11110...
    // التسلسل يُقرأ من الأبناء الفعليين للمجموعة (ParentCode) لا من بداية الرمز، ومن عدّاد مستقل لكل مجموعة.

    /// <summary>مفتاح عدّاد المجموعة (منفصل عن عدّادات العرض الثابت <see cref="HierarchicalCodes.AccountChildKey"/>).</summary>
    public static string CounterKey(string group) => $"treasury_code:{group}";

    /// <summary>هل الأب مجموعة نقدية تُرقَّم فروعها المباشرة آلياً: الصناديق 111، أو البنوك 110 إن كانت حساب نظام (<see cref="Roots"/>).</summary>
    public static async Task<bool> IsNumberedGroupAsync(ErpDbContext db, string? parentCode, CancellationToken ct) => parentCode switch
    {
        DefaultAccounts.CashBoxes => db.Set<Account>().Local.Any(a => a.Code == parentCode) || await db.Set<Account>().AnyAsync(a => a.Code == parentCode, ct),
        DefaultAccounts.Banks => db.Set<Account>().Local.Any(a => a.Code == parentCode && a.IsSystem) || await db.Set<Account>().AnyAsync(a => a.Code == parentCode && a.IsSystem, ct),
        _ => false,
    };

    /// <summary>تسلسل الرمز تحت المجموعة: المجموعة ثم رقم موجب بلا صفر بادئ (1103 ← 3، 11010 ← 10)، وإلا null.</summary>
    public static long? SequenceOf(string code, string group)
    {
        if (code.Length <= group.Length || code.Length > group.Length + 9 || !code.StartsWith(group, StringComparison.Ordinal)) return null;
        var tail = code.AsSpan(group.Length);
        if (tail[0] == '0') return null;
        foreach (var ch in tail) if (ch is < '0' or > '9') return null;
        return long.Parse(tail, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>رمز التسلسل <paramref name="sequence"/> تحت المجموعة (1101، 11010).</summary>
    public static string Compose(string group, long sequence) => group + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// يصرف رمز الحساب التالي تحت مجموعة النقدية <paramref name="group"/> من عدّادها المستقل، لكل الشاشات: البنك من شاشة البنوك أو
    /// الشجرة، والصندوق من الشجرة أو صندوق الموقع. يبدأ بعد أكبر تسلسل بين الأبناء الفعليين للمجموعة وقيمة العدّاد، فلا يُعاد رقم حُذف
    /// حسابه، ويتخطى أي رمز مستخدم في الدليل كله (ولو تحت أب آخر). الطلبات المتزامنة تحت المجموعة نفسها تنتظر بالدور (قفل المعاملة)،
    /// والعدّاد المتفائل والفهرس الفريد ضمانان إضافيان. يُستدعى داخل معاملة إنشاء الحساب.
    /// </summary>
    public static async Task<string> NextCodeAsync(ErpDbContext db, string group, CancellationToken ct)
    {
        var key = CounterKey(group);
        await HierarchicalCodes.LockAsync(db, key, ct);
        var taken = await TakenCodesAsync(db, group, ct);
        var start = Math.Max(MaxChildSequence(taken.Children, group), await LastValueAsync(db, key, ct)) + 1;
        var sequence = start;
        while (taken.All.Contains(Compose(group, sequence))) sequence++;
        // يُحجز من العدّاد ما يصل إلى التسلسل المختار دفعة واحدة (ما تخطاه لرموز مستخدمة لا يُعاد)
        var count = (int)Math.Max(1, sequence - await LastValueAsync(db, key, ct));
        var code = Compose(group, await NumberSequenceService.ReserveAsync(db, key, string.Empty, count, ct) + count - 1);
        // طلب سبقنا رغم القفل (بلا معاملة) فأنشأ الرمز: نتقدم بعده
        while (db.Set<Account>().Local.Any(a => a.Code == code) || await db.Set<Account>().AnyAsync(a => a.Code == code, ct))
            code = Compose(group, await NumberSequenceService.ReserveAsync(db, key, string.Empty, 1, ct));
        return code;
    }

    /// <summary>الرمز المتوقع التالي تحت المجموعة بلا صرف (للعرض في نوافذ الإضافة)؛ الفعلي يُصرف عند الحفظ وقد يتقدم إن سبقه طلب متزامن.</summary>
    public static async Task<string?> PeekCodeAsync(ErpDbContext db, string group, CancellationToken ct)
    {
        var taken = await TakenCodesAsync(db, group, ct);
        var sequence = Math.Max(MaxChildSequence(taken.Children, group), await LastValueAsync(db, CounterKey(group), ct)) + 1;
        while (taken.All.Contains(Compose(group, sequence))) sequence++;
        return Compose(group, sequence);
    }

    private static long MaxChildSequence(IEnumerable<string> children, string group)
        => children.Select(c => SequenceOf(c, group) ?? 0).DefaultIfEmpty(0).Max();

    /// <summary>رموز الأبناء الفعليين للمجموعة، وكل رموز الدليل بشكل تسلسلها (للتخطي)، شاملة المضاف في المعاملة ولم يُحفظ.</summary>
    private static async Task<(List<string> Children, HashSet<string> All)> TakenCodesAsync(ErpDbContext db, string group, CancellationToken ct)
    {
        var rows = await db.Set<Account>().AsNoTracking()
            .Where(a => a.ParentCode == group || (a.Code.StartsWith(group) && a.Code.Length > group.Length))
            .Select(a => new { a.Code, a.ParentCode }).ToListAsync(ct);
        var local = db.Set<Account>().Local.Select(a => new { a.Code, a.ParentCode }).ToList();
        var all = rows.Concat(local).ToList();
        return (all.Where(a => a.ParentCode == group).Select(a => a.Code).ToList(), all.Select(a => a.Code).ToHashSet(StringComparer.Ordinal));
    }

    private static async Task<long> LastValueAsync(ErpDbContext db, string key, CancellationToken ct)
        => await db.Set<NumberSequence>().AsNoTracking().Where(s => s.Key == key).Select(s => (long?)s.LastValue).FirstOrDefaultAsync(ct) ?? 0;

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
