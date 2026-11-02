using System.Globalization;
using System.Text.RegularExpressions;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// ترقيم المستندات بنطاق المنشأة: tenant = تسلسل واحد، warehouse = تسلسل لكل مستودع (معرض/مخزن) برمزه (SINV-RUH-000001)،
/// user = تسلسل لكل مستخدم برمزه (SINV-U03-000001). رموز العملاء والموردين والبنوك وتذاكر الدعم للمنشأة دائماً.
/// إعداد نوع المستند (على صف مفتاحه العام): يدوي/تلقائي، البادئة ({YYYY}/{YY})، الطول، النطاق، والتصفير لكل سنة مالية.
/// الصرف بقفل تزامن متفائل على صف العدّاد (لا MAX()+1)، والفهارس الفريدة على أرقام المستندات هي الضمان الأخير.
/// </summary>
public class NumberSequenceService : INumberSequenceService
{
    /// <summary>رموز بيانات أساسية مرتبطة بالدليل المحاسبي أو بالمنصة: لا تتبع نطاق ترقيم المستندات.</summary>
    private static readonly HashSet<string> CompanyWideKeys = new(StringComparer.OrdinalIgnoreCase) { "customer", "supplier", "bank", "support_ticket" };
    /// <summary>الرقم اليدوي: حروف لاتينية وأرقام وفواصل (- _ / .)، يبدأ بحرف أو رقم.</summary>
    private static readonly Regex ManualPattern = new("^[A-Za-z0-9][A-Za-z0-9\\-_/.]*$", RegexOptions.Compiled);
    internal const int ManualMaxLength = 40;
    internal const int DefaultPadding = 6;

    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;
    public NumberSequenceService(ErpDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<string> NextAsync(string key, string defaultPrefix, CancellationToken ct = default) => NextAsync(key, defaultPrefix, (Guid?)null, ct);

    /// <summary>الرقم التالي بنطاق المنشأة؛ <paramref name="warehouseId"/> مستودع المستند نفسه (فارغ = مستودع الجلسة). تلقائي دائماً.</summary>
    public Task<string> NextAsync(string key, string defaultPrefix, Guid? warehouseId, CancellationToken ct = default)
        => AssignAsync(new DocumentNumberRequest { Key = key, DefaultPrefix = defaultPrefix, WarehouseId = warehouseId }, ct);

    public async Task<string> AssignAsync(DocumentNumberRequest r, CancellationToken ct = default)
    {
        var config = await ConfigAsync(r.Key, r.DefaultPrefix, ct);
        if (r.UserEntered && config.Manual) return await ManualAsync(r, ct);

        // رقم تلقائي صادف رقماً أُدخل يدوياً من قبل (التبديل بين الوضعين): يُتجاوز للتالي
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var counter = await ResolveAsync(r.Key, config, r.WarehouseId, r.Date, ct);
            var value = await NextRawAsync(counter.Key, counter.Prefix, config.Padding, ct);
            var number = Format(counter.Prefix, value, config.Padding);
            if (r.Exists == null || !await r.Exists(number, ct)) return number;
        }
        throw new ConflictException(Messages.DocumentNumberGenerationFailed);
    }

    public async Task<DocumentCounterInfo> CounterAsync(string key, string defaultPrefix, Guid? warehouseId, DateTime? date, CancellationToken ct = default)
    {
        var config = await ConfigAsync(key, defaultPrefix, ct);
        var counter = await ResolveAsync(key, config, warehouseId, date, ct);
        var last = await _db.Set<NumberSequence>().AsNoTracking().Where(s => s.Key == counter.Key).Select(s => (long?)s.LastValue).FirstOrDefaultAsync(ct) ?? 0;
        return new DocumentCounterInfo
        {
            CounterKey = counter.Key, Prefix = counter.Prefix, Padding = config.Padding, LastValue = last,
            NextFormatted = Format(counter.Prefix, last + 1, config.Padding), FiscalYear = counter.FiscalYear, Manual = config.Manual,
        };
    }

    // ---------------- الإعداد الفعّال ----------------
    private sealed record Config(string Prefix, int Padding, bool Manual, string? Scope, bool ResetYearly);
    private sealed record Counter(string Key, string Prefix, int? FiscalYear);

    /// <summary>إعداد النوع من صف مفتاحه العام. بلا صف أو معطّل: الترقيم التلقائي الافتراضي.</summary>
    private async Task<Config> ConfigAsync(string key, string defaultPrefix, CancellationToken ct)
    {
        var type = DocumentNumberTypes.Find(key);
        var row = await _db.Set<NumberSequence>().AsNoTracking().Where(s => s.Key == key)
            .Select(s => new { s.Prefix, s.Padding, s.Mode, s.Scope, s.ResetYearly, s.IsActive }).FirstOrDefaultAsync(ct);
        if (row == null) return new Config(defaultPrefix, DefaultPadding, false, null, false);
        if (!row.IsActive) return new Config(type?.DefaultPrefix ?? defaultPrefix, DefaultPadding, false, null, false);
        return new Config(row.Prefix, row.Padding > 0 ? row.Padding : DefaultPadding, row.Mode == "manual" && type?.ManualAllowed == true, row.Scope, row.ResetYearly);
    }

    /// <summary>
    /// العدّاد الذي يُصرف منه: المفتاح العام، أو مفتاح نطاقه (المستودع/المستخدم) وسنته المالية عند التصفير السنوي.
    /// النطاق من بيانات المستند نفسه (مستودعه)، وما لا مستودع له (جلسة «كل المواقع») يبقى على تسلسل المنشأة.
    /// </summary>
    private async Task<Counter> ResolveAsync(string key, Config config, Guid? warehouseId, DateTime? date, CancellationToken ct)
    {
        int? year = config.ResetYearly || HasYearToken(config.Prefix) ? await FiscalYearAsync(date is { } d && d != default ? d : SaudiTime.Today, ct) : null;
        var basePrefix = ApplyYear(config.Prefix, year);
        var yearSuffix = config.ResetYearly && year.HasValue ? $"@fy:{year}" : string.Empty;
        if (CompanyWideKeys.Contains(key)) return new Counter(key, basePrefix, year);

        var scope = config.Scope ?? await _db.Set<Tenant>().AsNoTracking().Select(t => t.DocumentNumberingScope).FirstOrDefaultAsync(ct) ?? "tenant";
        if (scope == "warehouse" && (warehouseId ?? _db.SessionWarehouseId) is Guid id)
        {
            var code = Clean(await _db.Set<Warehouse>().AsNoTracking().Where(w => w.Id == id).Select(w => w.Code).FirstOrDefaultAsync(ct));
            if (code.Length > 0) return new Counter($"{key}@warehouse:{id}{yearSuffix}", $"{basePrefix}{code}-", year);
        }
        else if (scope == "user" && _user.UserId is Guid userId && await UserCodeAsync(userId, ct) is { } userCode)
        {
            return new Counter($"{key}@user:{userId}{yearSuffix}", $"{basePrefix}{userCode}-", year);
        }
        return new Counter(key + yearSuffix, basePrefix, year);
    }

    /// <summary>السنة المالية لتاريخ (سنة بدايتها) من بداية السنة المالية للمنشأة (MM-dd)؛ بلا إعداد = السنة الميلادية.</summary>
    private async Task<int> FiscalYearAsync(DateTime date, CancellationToken ct)
    {
        var start = await _db.Set<Tenant>().AsNoTracking().Select(t => t.FinancialYearStart).FirstOrDefaultAsync(ct);
        return FiscalYearOf(date, start);
    }

    internal static int FiscalYearOf(DateTime date, string? financialYearStart)
    {
        if (!DateTime.TryParseExact(financialYearStart, "MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var s)) return date.Year;
        var startThisYear = new DateTime(date.Year, s.Month, Math.Min(s.Day, DateTime.DaysInMonth(date.Year, s.Month)));
        return date.Date < startThisYear ? date.Year - 1 : date.Year;
    }

    internal static bool HasYearToken(string prefix) => prefix.Contains("{YYYY}", StringComparison.Ordinal) || prefix.Contains("{YY}", StringComparison.Ordinal);

    private static string ApplyYear(string prefix, int? year)
        => year is int y ? prefix.Replace("{YYYY}", y.ToString("0000")).Replace("{YY}", (y % 100).ToString("00")) : prefix;

    internal static string Format(string prefix, long value, int padding) => $"{prefix}{value.ToString(CultureInfo.InvariantCulture).PadLeft(padding, '0')}";

    // ---------------- اليدوي ----------------
    private static async Task<string> ManualAsync(DocumentNumberRequest r, CancellationToken ct)
    {
        var number = (r.ManualNumber ?? string.Empty).Trim();
        if (number.Length == 0) throw new ValidationFailedException(Messages.DocumentNumberRequired);
        if (number.Length > ManualMaxLength || !ManualPattern.IsMatch(number))
            throw new ValidationFailedException(string.Format(Messages.DocumentNumberInvalid, ManualMaxLength));
        if (r.Exists != null && await r.Exists(number, ct)) throw new ConflictException(string.Format(Messages.DocumentNumberExists, number));
        return number;
    }

    /// <summary>رمز المستخدم في الأرقام: يُمنح عند أول مستند بالترقيم لكل مستخدم (U01، U02...) ويبقى ثابتاً.</summary>
    private async Task<string?> UserCodeAsync(Guid userId, CancellationToken ct)
    {
        var user = await _db.Set<User>().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null) return null;
        if (!string.IsNullOrWhiteSpace(user.DocumentCode)) return user.DocumentCode;
        var n = await ReserveAsync("user_document_code", "U", 1, ct);
        user.DocumentCode = "U" + n.ToString("00");
        await _db.SaveChangesAsync(ct);
        return user.DocumentCode;
    }

    private static string Clean(string? code) => new string((code ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    // ---------------- الصرف ----------------
    public async Task<long> ReserveAsync(string key, string defaultPrefix, int count, CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var seq = await _db.Set<NumberSequence>().FirstOrDefaultAsync(s => s.Key == key, ct);
            var isNew = seq == null;
            if (seq == null)
            {
                seq = new NumberSequence { Key = key, Prefix = defaultPrefix, LastValue = 0 };
                _db.Add(seq);
            }
            var first = seq.LastValue + 1;
            seq.LastValue += count;
            seq.Version = Guid.NewGuid();
            try
            {
                await _db.SaveChangesAsync(ct);
                return first;
            }
            catch (DbUpdateConcurrencyException ex) when (ex.Entries.Any(e => e.Entity is NumberSequence))
            {
                _db.Entry(seq).State = EntityState.Detached;
            }
            catch (DbUpdateException) when (isNew)
            {
                _db.Entry(seq).State = EntityState.Detached;
            }
        }
        throw new ConflictException(Messages.DocumentNumberGenerationFailed);
    }

    /// <summary>يزيد العدّاد بقفل تزامن متفائل (يُنشأ عند أول استخدام) ويعيد قيمته الجديدة.</summary>
    private async Task<long> NextRawAsync(string key, string prefix, int padding, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var seq = await _db.Set<NumberSequence>().FirstOrDefaultAsync(s => s.Key == key, ct);
            var isNew = seq == null;
            if (seq == null)
            {
                seq = new NumberSequence { Key = key, Prefix = prefix, Padding = padding, LastValue = 0 };
                _db.Add(seq);
            }
            seq.LastValue++;
            seq.Version = Guid.NewGuid();
            try
            {
                await _db.SaveChangesAsync(ct);
                return seq.LastValue;
            }
            catch (DbUpdateConcurrencyException ex) when (ex.Entries.Any(e => e.Entity is NumberSequence))
            {
                _db.Entry(seq).State = EntityState.Detached; // تزامن: أعد المحاولة بقيمة محدَّثة
            }
            catch (DbUpdateException) when (isNew)
            {
                _db.Entry(seq).State = EntityState.Detached; // سباق إنشاء العدّاد لأول مرة
            }
        }
        throw new ConflictException(Messages.DocumentNumberGenerationFailed);
    }
}
