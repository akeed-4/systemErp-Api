using System.Globalization;
using ERP.Core.Contracts.Shared;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// الترقيم الهرمي المركزي (مراكز التكلفة والحسابات المخصصة للكيانات): رمز الابن = رمز الأب + تسلسل بعرض ثابت مكمَّل بأصفار.
/// التسلسل من عدّاد لكل أب (<see cref="NumberSequence"/> بقفل تزامن متفائل)، فلا يُعاد رقم حُذف صاحبه ولا يتكرر رقم بين طلبين متزامنين،
/// ويبدأ بعد أكبر رمز قائم بالشكل نفسه (بيانات قديمة أو رموز أُدخلت يدوياً). الفهرس الفريد على (المنشأة، الرمز) هو الضمان الأخير.
/// يُستدعى داخل معاملة إنشاء السجل، فلا يُستهلك رقم لسجل لم يُحفظ.
/// </summary>
internal static class HierarchicalCodes
{
    /// <summary>تسلسل الرمز إن كان بشكل «البادئة + <paramref name="width"/> أرقام»، وإلا null (رمز بشكل آخر لا يؤثر في التسلسل).</summary>
    public static long? SequenceOf(string code, string prefix, int width)
    {
        if (code.Length != prefix.Length + width || !code.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var tail = code.AsSpan(prefix.Length);
        foreach (var ch in tail) if (ch is < '0' or > '9') return null;
        return long.Parse(tail, NumberStyles.None, CultureInfo.InvariantCulture);
    }

    /// <summary>البادئة ثم التسلسل بعرض ثابت بأصفار بادئة (1101 + 1 بعرض 4 = 11010001).</summary>
    public static string Compose(string prefix, long sequence, int width)
        => prefix + sequence.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');

    /// <summary>أكبر تسلسل يتسع له العرض (99 لعرض 2، 9999 لعرض 4).</summary>
    public static long MaxSequence(int width) => (long)Math.Pow(10, width) - 1;

    /// <summary>
    /// يحجز الرمز التالي تحت <paramref name="prefix"/>. <paramref name="codes"/>: رموز الجدول المعني (مرشّحة بالمنشأة)، يُقرأ منها
    /// ما يطابق شكل البادئة والعرض فقط. يرمي <see cref="ConflictException"/> برسالة <paramref name="exhaustedMessage"/> إن امتلأ العرض.
    /// </summary>
    public static Task<string> NextAsync(ErpDbContext db, INumberSequenceService numbers, string counterKey, string prefix, int width,
        IQueryable<string> codes, string exhaustedMessage, CancellationToken ct)
        => NextAsync(db, (key, count, token) => numbers.ReserveAsync(key, string.Empty, count, token), counterKey, prefix, width, codes, exhaustedMessage, ct);

    /// <summary>مثل السابق على السياق مباشرة (للمساعدات الثابتة بلا خدمة ترقيم محقونة، كحسابات المواقع).</summary>
    public static Task<string> NextAsync(ErpDbContext db, string counterKey, string prefix, int width,
        IQueryable<string> codes, string exhaustedMessage, CancellationToken ct)
        => NextAsync(db, (key, count, token) => NumberSequenceService.ReserveAsync(db, key, string.Empty, count, token), counterKey, prefix, width, codes, exhaustedMessage, ct);

    private static async Task<string> NextAsync(ErpDbContext db, Func<string, int, CancellationToken, Task<long>> reserve, string counterKey, string prefix, int width,
        IQueryable<string> codes, string exhaustedMessage, CancellationToken ct)
    {
        await LockAsync(db, counterKey, ct);
        var existingMax = await ExistingMaxAsync(codes, prefix, width, ct);
        if (existingMax >= MaxSequence(width)) throw new ConflictException(exhaustedMessage);

        // العدّاد يتقدم دائماً؛ وإن سبقته رموز قائمة (قبل العدّاد أو مُدخلة يدوياً) يُحجز ما يتخطاها دفعة واحدة.
        var last = await LastValueAsync(db, counterKey, ct);
        var count = (int)Math.Max(1, existingMax - last + 1);
        var first = await reserve(counterKey, count, ct);
        var sequence = first + count - 1;
        if (sequence > MaxSequence(width)) throw new ConflictException(exhaustedMessage);
        return Compose(prefix, sequence, width);
    }

    /// <summary>
    /// الرمز الذي سيُصرف تالياً بلا حجز (للعرض قبل الحفظ)، أو null إن امتلأ العرض. الرمز الفعلي يُصرف عند الحفظ، وقد يتقدم
    /// إن سبقه طلب متزامن.
    /// </summary>
    public static async Task<string?> PeekAsync(ErpDbContext db, string counterKey, string prefix, int width, IQueryable<string> codes, CancellationToken ct)
    {
        var next = Math.Max(await ExistingMaxAsync(codes, prefix, width, ct), await LastValueAsync(db, counterKey, ct)) + 1;
        return next > MaxSequence(width) ? null : Compose(prefix, next, width);
    }

    // ---------- الحسابات الفرعية في الدليل ----------

    /// <summary>عدّاد الحسابات الفرعية تحت أب بعرض معيّن: مفتاح واحد لكل الشاشات التي تنشئ تحته (الكيانات، النقدية، المواقع، الأصول).</summary>
    public static string AccountChildKey(string parentCode, int width) => $"account_child_code:{parentCode}:{width}";

    /// <summary>يصرف رمز الحساب الفرعي التالي تحت <paramref name="parentCode"/> (الأب + <paramref name="width"/> خانات) من عدّاد الأب.</summary>
    public static Task<string> NextAccountChildAsync(ErpDbContext db, string parentCode, int width, string exhaustedMessage, CancellationToken ct)
        => NextAsync(db, AccountChildKey(parentCode, width), parentCode, width, db.Set<Account>().Select(a => a.Code), exhaustedMessage, ct);

    /// <summary>الرمز الفرعي المتوقع التالي بلا صرف (للعرض).</summary>
    public static Task<string?> PeekAccountChildAsync(ErpDbContext db, string parentCode, int width, CancellationToken ct)
        => PeekAsync(db, AccountChildKey(parentCode, width), parentCode, width, db.Set<Account>().Select(a => a.Code), ct);

    private static async Task<long> ExistingMaxAsync(IQueryable<string> codes, string prefix, int width, CancellationToken ct)
    {
        var length = prefix.Length + width;
        var sameShape = await codes.Where(c => c.Length == length && c.StartsWith(prefix)).ToListAsync(ct);
        return sameShape.Select(c => SequenceOf(c, prefix, width) ?? 0).DefaultIfEmpty(0).Max();
    }

    private static async Task<long> LastValueAsync(ErpDbContext db, string counterKey, CancellationToken ct)
        => await db.Set<NumberSequence>().AsNoTracking().Where(s => s.Key == counterKey)
            .Select(s => (long?)s.LastValue).FirstOrDefaultAsync(ct) ?? 0;

    /// <summary>
    /// قفل تطبيقي على المعاملة لعدّاد الأب (<c>sp_getapplock</c>): الطلبات المتزامنة تحت الأب نفسه تنتظر بالدور بدل أن تستنفد
    /// محاولات العدّاد المتفائلة، ويُحرَّر القفل مع إتمام المعاملة أو تراجعها. بلا معاملة يبقى قفل العدّاد المتفائل وحده.
    /// </summary>
    internal static async Task LockAsync(ErpDbContext db, string counterKey, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction == null) return;
        var resource = $"hc:{db.CurrentTenantId:N}:{counterKey}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DECLARE @r int; EXEC @r = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000; IF @r < 0 THROW 51000, 'code sequence lock timeout', 1;",
            ct);
    }
}
