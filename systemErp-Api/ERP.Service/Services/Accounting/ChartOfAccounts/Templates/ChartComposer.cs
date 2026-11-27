namespace ERP.Service.Services.Accounting;

/// <summary>
/// دمج القوالب (دالة صرفة بلا قاعدة بيانات): الأساسي ثم النشاط ثم الموديولات، فوق شجرة المنشأة القائمة (فارغة لمنشأة جديدة).
/// <list type="bullet">
/// <item>الحساب نفسه من أكثر من قالب يُدمج في حساب واحد: بالمفتاح الوظيفي أولاً ثم بالرقم (الإلزامي يغلب، وقالب النشاط قد يسمّي حساب الأساسي).</item>
/// <item>المفتاح المربوط فعلاً في المنشأة لا يُنشأ له حساب ثانٍ.</item>
/// <item>الرقم المحجوز بنوعه وأبيه في الشجرة القديمة، ولا يُولَّد رقم محجوز لحساب آخر.</item>
/// <item>كل حساب له أب موجود (في الدمج أو المنشأة) من نوعه نفسه، والأب يصير رئيسياً.</item>
/// </list>
/// </summary>
internal static class ChartComposer
{
    internal sealed class ExistingAccount
    {
        public string Code { get; init; } = string.Empty;
        public string NameAr { get; init; } = string.Empty;
        public string NameEn { get; init; } = string.Empty;
        public string? ParentCode { get; init; }
        public AccountCategory Type { get; init; }
        public int Level { get; init; }
        public bool IsGroup { get; init; }
    }

    internal sealed class Row
    {
        public string? Code { get; set; }
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string? ParentCode { get; set; }
        public AccountCategory Type { get; set; }
        public bool IsGroup { get; set; }
        public string? SemanticKey { get; set; }
        public bool IsRequired { get; set; }
        public int Level { get; set; }
        public bool CodeGenerated { get; set; }
        /// <summary>المفتاح مربوط في المنشأة بهذا الحساب القائم: لا يُنشأ شيء للسطر.</summary>
        public string? BoundTo { get; set; }
        public ChartTemplateKind FirstKind { get; set; }
        public List<string> Sources { get; } = new();
    }

    internal sealed class Result
    {
        public List<string> Templates { get; } = new();
        public List<Row> Rows { get; } = new();
        public List<string> Errors { get; } = new();
        /// <summary>حسابات قائمة فرعية ستصير رئيسية لأن حساباً من القوالب يُنشأ تحتها.</summary>
        public HashSet<string> ExistingParentsToGroup { get; } = new();
    }

    public static Result Compose(IEnumerable<ChartTemplate> templates, IReadOnlyCollection<ExistingAccount> existing, Func<string, string?> boundCode)
    {
        var result = new Result();
        var existingByCode = existing.ToDictionary(a => a.Code);
        var byCode = new Dictionary<string, Row>();
        var byKey = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);

        foreach (var template in templates)
        {
            result.Templates.Add($"{template.Key}@{template.Version}");
            foreach (var a in template.Accounts.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.Code, StringComparer.Ordinal))
                Merge(result, template, a, byCode, byKey);
        }

        // المفاتيح المربوطة فعلاً في المنشأة: السطر محقق بحسابه القائم
        foreach (var row in result.Rows.Where(r => r.SemanticKey != null))
            if (boundCode(row.SemanticKey!) is { } bound && existingByCode.ContainsKey(bound))
                row.BoundTo = bound;

        bool Known(string code) => existingByCode.ContainsKey(code) || byCode.ContainsKey(code);
        AccountCategory? TypeOf(string code)
            => byCode.TryGetValue(code, out var r) ? r.Type : existingByCode.TryGetValue(code, out var e) ? e.Type : null;

        // الآباء والأنواع للأرقام الصريحة، ثم ترقيم الأدوار تحت آبائها
        foreach (var row in result.Rows.Where(r => r.BoundTo == null))
        {
            if (row.ParentCode == null) continue;
            if (!Known(row.ParentCode))
                result.Errors.Add(string.Format(Messages.ChartTemplateParentMissing, Label(row), row.ParentCode));
            else if (TypeOf(row.ParentCode) != row.Type)
                result.Errors.Add(string.Format(Messages.ChartTemplateParentTypeMismatch, Label(row), row.ParentCode));
        }
        foreach (var row in result.Rows.Where(r => r.Code == null && r.BoundTo == null))
        {
            if (row.ParentCode == null || !Known(row.ParentCode)) continue;
            row.Code = NextCode(row.ParentCode, existing, result.Rows, Known);
            row.CodeGenerated = true;
            byCode[row.Code] = row;
        }
        foreach (var row in result.Rows.Where(r => r.Code == null && r.BoundTo == null && r.ParentCode == null))
            result.Errors.Add(string.Format(Messages.ChartTemplateCodeRequiredForRoot, Label(row)));

        // المستويات والحسابات الرئيسية
        var active = result.Rows.Where(r => r.BoundTo == null && r.Code != null).ToList();
        var parentsInUse = active.Select(r => r.ParentCode).OfType<string>().ToHashSet();
        foreach (var row in active)
        {
            row.Level = LevelOf(row.Code!, byCode, existingByCode, 0);
            if (parentsInUse.Contains(row.Code!)) row.IsGroup = true;
        }
        foreach (var code in parentsInUse)
            if (!byCode.ContainsKey(code) && existingByCode.TryGetValue(code, out var e) && !e.IsGroup)
                result.ExistingParentsToGroup.Add(code);

        result.Rows.Sort((x, y) => string.CompareOrdinal(x.BoundTo ?? x.Code ?? "~", y.BoundTo ?? y.Code ?? "~"));
        return result;
    }

    private static void Merge(Result result, ChartTemplate template, ChartTemplateAccount a, Dictionary<string, Row> byCode, Dictionary<string, Row> byKey)
    {
        var code = string.IsNullOrWhiteSpace(a.Code) ? null : a.Code.Trim();
        var key = string.IsNullOrWhiteSpace(a.SemanticKey) ? null : AccountSemanticKeys.Normalize(a.SemanticKey);
        var parent = string.IsNullOrWhiteSpace(a.ParentCode) ? null : a.ParentCode.Trim();
        var label = $"{code ?? key ?? a.NameAr} ({template.Key})";

        if (string.IsNullOrWhiteSpace(a.NameAr)) { result.Errors.Add(string.Format(Messages.ChartTemplateAccountNameRequired, label)); return; }
        if (code != null && !code.All(char.IsDigit)) { result.Errors.Add(string.Format(Messages.ChartTemplateCodeDigits, label)); return; }
        if (code != null && parent != null && !code.StartsWith(parent, StringComparison.Ordinal) && ReservedAccountCodes.Find(code) == null)
        { result.Errors.Add(string.Format(Messages.ChartTemplateCodeOutsideParent, label, parent)); return; }
        if (key != null && !AccountSemanticKeys.IsValidFormat(key)) { result.Errors.Add(string.Format(Messages.ChartTemplateSemanticKeyInvalid, key)); return; }
        if (ReservedAccountCodes.Find(code) is { } reserved && (reserved.Type != a.Type || reserved.ParentCode != parent))
        { result.Errors.Add(string.Format(Messages.ChartTemplateReservedCodeChanged, code, reserved.NameAr)); return; }
        if (code == null && ReservedAccountCodes.IsReserved(AccountSemanticKeys.Of(key ?? "").DefaultCode)
            && AccountSemanticKeys.Of(key!).Store == AccountSemanticKeys.ReservedStore)
        { result.Errors.Add(string.Format(Messages.ChartTemplateReservedKeyNeedsCode, key, AccountSemanticKeys.Of(key!).DefaultCode)); return; }

        Row? row = null;
        if (key != null && byKey.TryGetValue(key, out var sameKey)) row = sameKey;
        else if (code != null && byCode.TryGetValue(code, out var sameCode)) row = sameCode;

        if (row == null)
        {
            row = new Row
            {
                Code = code, NameAr = a.NameAr.Trim(), NameEn = string.IsNullOrWhiteSpace(a.NameEn) ? a.NameAr.Trim() : a.NameEn.Trim(),
                ParentCode = parent, Type = a.Type, IsGroup = a.IsGroup, SemanticKey = key, IsRequired = a.IsRequired, FirstKind = template.Kind,
            };
            row.Sources.Add(template.Key);
            result.Rows.Add(row);
            if (code != null) byCode[code] = row;
            if (key != null) byKey[key] = row;
            return;
        }

        // الحساب نفسه من قالب آخر
        if (code != null && row.Code != null && code != row.Code)
        { result.Errors.Add(string.Format(Messages.ChartTemplateKeyOnTwoCodes, key, row.Code, code)); return; }
        if (row.Type != a.Type || (parent != row.ParentCode && code != null && row.Code != null))
        { result.Errors.Add(string.Format(Messages.ChartTemplateMergeConflict, code ?? row.Code ?? key, template.Key)); return; }
        if (key != null && row.SemanticKey != null && !string.Equals(key, row.SemanticKey, StringComparison.OrdinalIgnoreCase))
        { result.Errors.Add(string.Format(Messages.ChartTemplateTwoKeysOnCode, code ?? row.Code, row.SemanticKey, key)); return; }

        if (row.Code == null && code != null)
        {
            if (byCode.ContainsKey(code)) { result.Errors.Add(string.Format(Messages.ChartTemplateMergeConflict, code, template.Key)); return; }
            row.Code = code; byCode[code] = row;
        }
        if (row.SemanticKey == null && key != null) { row.SemanticKey = key; byKey[key] = row; }
        row.IsRequired |= a.IsRequired;
        row.IsGroup |= a.IsGroup;
        // قالب النشاط يسمّي حساب القالب الأساسي باسم نشاطه (إيرادات الخدمات بدل إيرادات المبيعات)
        if (template.Kind == ChartTemplateKind.Activity && row.FirstKind == ChartTemplateKind.Base)
        {
            row.NameAr = a.NameAr.Trim();
            if (!string.IsNullOrWhiteSpace(a.NameEn)) row.NameEn = a.NameEn.Trim();
        }
        if (!row.Sources.Contains(template.Key)) row.Sources.Add(template.Key);
    }

    private static int LevelOf(string code, Dictionary<string, Row> rows, Dictionary<string, ExistingAccount> existing, int depth)
    {
        if (depth > 32) return depth; // حماية من حلقة في بيانات خاطئة
        if (existing.TryGetValue(code, out var e)) return e.Level;
        if (!rows.TryGetValue(code, out var r) || r.ParentCode == null) return 1;
        return LevelOf(r.ParentCode, rows, existing, depth + 1) + 1;
    }

    /// <summary>
    /// الرقم التالي تحت الأب بقاعدة تهيئة حسابات العقود (بعد أكبر رقم بين إخوته وبطوله، ضمن بادئة الأب إن كان الإخوة عليها، وإلا خانة
    /// إضافية)، مع تخطي الأرقام المحجوزة: حساب جديد لا يأخذ رقماً يقرؤه النظام لغرض آخر.
    /// </summary>
    private static string NextCode(string parent, IReadOnlyCollection<ExistingAccount> existing, List<Row> rows, Func<string, bool> known)
    {
        bool Taken(string c) => known(c) || ReservedAccountCodes.IsReserved(c);
        var siblings = existing.Where(a => a.ParentCode == parent).Select(a => a.Code)
            .Concat(rows.Where(r => r.ParentCode == parent && r.Code != null && r.BoundTo == null).Select(r => r.Code!))
            .Where(c => c.All(char.IsDigit)).Distinct().ToList();
        if (siblings.Count > 0)
        {
            var width = siblings.GroupBy(c => c.Length).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
            var sameWidth = siblings.Where(c => c.Length == width).ToList();
            var prefixed = siblings.All(c => c.StartsWith(parent, StringComparison.Ordinal));
            for (var next = sameWidth.Max(long.Parse) + 1; next.ToString().Length == width; next++)
            {
                var code = next.ToString();
                if (Taken(code)) continue;
                if (!prefixed || code.StartsWith(parent, StringComparison.Ordinal)) return code;
                break;
            }
        }
        var minWidth = Math.Max(parent.Length + 1, siblings.Count > 0 ? siblings.Max(c => c.Length) + 1 : 0);
        for (var width = minWidth; ; width++)
        {
            var digits = width - parent.Length;
            var limit = (long)Math.Pow(10, digits);
            for (long n = 1; n < limit; n++)
            {
                var code = parent + n.ToString().PadLeft(digits, '0');
                if (!Taken(code)) return code;
            }
        }
    }

    private static string Label(Row r) => $"{r.Code ?? r.SemanticKey} {r.NameAr}";
}
