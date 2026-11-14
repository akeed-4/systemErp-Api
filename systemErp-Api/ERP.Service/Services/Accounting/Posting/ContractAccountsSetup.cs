using ERP.Core.DTOs.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// تهيئة حسابات العقود (الدفعات المقدمة والمحتجزات والغرامات والخصومات) بلا أرقام ثابتة:
/// <list type="number">
/// <item>إعداد محدد مسبقاً (اختيار المستخدم) لا يُمس؛ إن كان غير صالح يُبلَّغ عنه فقط.</item>
/// <item>إعداد فارغ: حساب قائم مناسب يُعاد استخدامه — باسم معروف لهذا الغرض، نوعه يناسب الدور، فرعي، ليس حساب طرف/بنك/معرض
/// ولا حساباً لدور آخر. أكثر من مرشّح = مشكلة تُبلَّغ (لا تخمين).</item>
/// <item>وإلا يُنشأ تحت المجموعة التي فيها حسابات المنشأة المماثلة فعلاً (الالتزامات بجوار ضريبة المخرجات، الأصول بجوار ضريبة
/// المدخلات، الإيرادات بجوار الإيرادات الأخرى، المصروفات بجوار التشغيلية)، وإلا تحت جذر النوع، برقم يتبع ترقيم إخوته.</item>
/// </list>
/// لا يُعدَّل حساب قائم ولا قيد. التشغيل المتكرر لا يكرر شيئاً: الإعداد المربوط يبقى، والحساب المُنشأ يُعرف باسمه لو مُسح ربطه.
/// تُستدعى عند إنشاء منشأة، ومن زر التهيئة في إعدادات الحسابات، وقبل فوترة العقود (للإعدادات الناقصة فقط).
/// </summary>
internal static class ContractAccountsSetup
{
    /// <summary>حساب في الشجرة كما يراه المخطط (القائم والمُخطط لإنشائه).</summary>
    internal sealed class Node
    {
        public string Code { get; init; } = string.Empty;
        public string NameAr { get; init; } = string.Empty;
        public string NameEn { get; init; } = string.Empty;
        public AccountCategory Type { get; init; }
        public string? ParentCode { get; init; }
        public int Level { get; init; }
        public LinkedEntityType? Linked { get; init; }
        public bool IsNew { get; init; }
    }

    /// <summary>دور حساب عقد: الإعداد، نوع الحساب المُنشأ، مراسي المجموعة (حسابات يُنشأ بجوارها)، وأسماؤه المعروفة لإعادة الاستخدام.</summary>
    private sealed record Spec(string Role, AccountCategory Type, string NameAr, string NameEn,
        Func<AccountingSettings, string?[]> Anchors, string[] Aliases);

    private static string?[] LiabilityAnchors(AccountingSettings s) => new[] { s.OutputVat(), DefaultAccounts.Payables };
    private static string?[] AssetAnchors(AccountingSettings s) => new[] { s.InputVat(), DefaultAccounts.Receivables };
    private static string?[] RevenueAnchors(AccountingSettings s)
        => new[] { DefaultAccounts.AssetDisposalGain, DefaultAccounts.CashOverage, DefaultAccounts.ForfeitedDeposits, s.SalesRevenue() };
    private static string?[] ExpenseAnchors(AccountingSettings s)
        => new[] { DefaultAccounts.PurchasedServices, DefaultAccounts.PettyCashExpenses, s.Cogs() };

    /// <summary>بالترتيب: العميل ثم المورد لكل نوع، فتأخذ الأرقام المتتالية الترتيب نفسه في كل منشأة.</summary>
    private static readonly Spec[] Specs =
    {
        new(nameof(AccountingSettings.ContractAdvancesReceivedAccountCode), AccountCategory.Liability,
            "دفعات مقدمة مستلمة على العقود", "Contract Advances Received", LiabilityAnchors,
            new[] { "دفعات مقدمة مستلمة", "دفعات مقدمة من العملاء", "دفعات مقدمة مستلمة من العملاء", "advances received", "customer advances", "advances from customers" }),
        new(nameof(AccountingSettings.ContractRetentionPayableAccountCode), AccountCategory.Liability,
            "محتجزات ضمان مستحقة لمقاولي الباطن", "Retention Payable to Subcontractors", LiabilityAnchors,
            new[] { "محتجزات ضمان مستحقة", "ضمانات محتجزة لمقاولي الباطن", "محتجزات حسن التنفيذ المستحقة", "retention payable", "retentions payable" }),
        new(nameof(AccountingSettings.ContractRetentionReceivableAccountCode), AccountCategory.Asset,
            "محتجزات ضمان لدى العملاء", "Retention Receivable from Customers", AssetAnchors,
            new[] { "ضمانات محتجزة لدى العملاء", "محتجزات حسن التنفيذ لدى العملاء", "retention receivable", "retentions receivable" }),
        new(nameof(AccountingSettings.ContractAdvancesPaidAccountCode), AccountCategory.Asset,
            "دفعات مقدمة مدفوعة لمقاولي الباطن", "Advances Paid to Subcontractors", AssetAnchors,
            new[] { "دفعات مقدمة مدفوعة على العقود", "دفعات مقدمة مدفوعة", "دفعات مقدمة للموردين", "advances paid", "advances to suppliers", "advances to subcontractors" }),
        new(nameof(AccountingSettings.ContractPenaltiesIncomeAccountCode), AccountCategory.Revenue,
            "إيراد غرامات العقود", "Contract Penalties Income", RevenueAnchors,
            new[] { "إيرادات غرامات العقود", "إيراد غرامات تأخير", "contract penalty income", "penalty income", "penalties income" }),
        new(nameof(AccountingSettings.ContractPenaltiesExpenseAccountCode), AccountCategory.Expense,
            "غرامات العقود", "Contract Penalties Expense", ExpenseAnchors,
            new[] { "مصروف غرامات العقود", "غرامات تأخير العقود", "contract penalties", "contract penalty expense", "penalty expense", "penalties expense" }),
        // الخصومات الأخرى (مثل خصم السعودة يستقطعه العميل منا) عبء على المنشأة؛ في عقد المورد تُقيَّد دائنة على الحساب نفسه فتخفّضه
        new(nameof(AccountingSettings.ContractOtherDeductionsAccountCode), AccountCategory.Expense,
            "خصومات العقود الأخرى", "Other Contract Deductions", ExpenseAnchors,
            new[] { "استقطاعات العقود الأخرى", "خصومات أخرى على العقود", "other contract deductions", "contract deductions" }),
        // مواد المشاريع: الصرف للمشروع أصل تحت التنفيذ حتى يُفوتر مستخلصه، ثم تكلفة إيرادات (لا مصروف مباشر عند الصرف)
        new(nameof(AccountingSettings.ProjectMaterialsWipAccountCode), AccountCategory.Asset,
            "مواد مشاريع تحت التنفيذ", "Project Materials Under Construction", AssetAnchors,
            new[] { "مواد مشاريع تحت التنفيذ", "أعمال تحت التنفيذ", "مشاريع تحت التنفيذ", "project materials under construction", "work in progress", "construction in progress" }),
        new(nameof(AccountingSettings.ContractCostOfRevenueAccountCode), AccountCategory.Expense,
            "تكلفة إيرادات العقود", "Contract Cost of Revenue", ExpenseAnchors,
            new[] { "تكلفة إيرادات العقود", "تكلفة إيرادات المشاريع", "تكلفة الإيرادات", "contract cost of revenue", "cost of revenue" }),
        // تحميل العمالة والمعدات المعتمدة في التقارير اليومية على المشروع، ومقابله المحمّل (دائن يقابل الرواتب والإهلاك الفعلية)
        new(nameof(AccountingSettings.ProjectLaborCostAccountCode), AccountCategory.Expense,
            "تكلفة عمالة المشاريع", "Project Labor Cost", ExpenseAnchors,
            new[] { "تكلفة عمالة المشاريع", "عمالة المشاريع", "project labor cost", "project labour cost" }),
        new(nameof(AccountingSettings.ProjectEquipmentCostAccountCode), AccountCategory.Expense,
            "تكلفة معدات المشاريع", "Project Equipment Cost", ExpenseAnchors,
            new[] { "تكلفة معدات المشاريع", "تشغيل معدات المشاريع", "project equipment cost" }),
        new(nameof(AccountingSettings.ProjectAbsorbedCostAccountCode), AccountCategory.Expense,
            "تكاليف محمّلة على المشاريع", "Costs Absorbed by Projects", ExpenseAnchors,
            new[] { "تكاليف محملة على المشاريع", "تكاليف محمّلة على المشاريع", "costs absorbed by projects", "absorbed costs" }),
        // استحقاق إيجار المعدات المستأجرة من التقارير اليومية حتى تصل فاتورة المؤجر
        new(nameof(AccountingSettings.ProjectRentedEquipmentCostAccountCode), AccountCategory.Expense,
            "تكلفة معدات مستأجرة للمشاريع", "Project Rented Equipment Cost", ExpenseAnchors,
            new[] { "تكلفة معدات مستأجرة للمشاريع", "إيجار معدات المشاريع", "project rented equipment cost", "project equipment rental" }),
        new(nameof(AccountingSettings.ProjectAccruedRentalAccountCode), AccountCategory.Liability,
            "إيجار معدات مستحق", "Accrued Equipment Rental", LiabilityAnchors,
            new[] { "إيجار معدات مستحق", "إيجارات معدات مستحقة", "accrued equipment rental", "accrued rental" }),
        // مشتريات المشاريع المستلمة قبل وصول فاتورة المورد
        new(nameof(AccountingSettings.ProjectGoodsReceivedNotInvoicedAccountCode), AccountCategory.Liability,
            "بضاعة مستلمة لم تُفوتر", "Goods Received Not Invoiced", LiabilityAnchors,
            new[] { "بضاعة مستلمة لم تفوتر", "بضاعة مستلمة لم تُفوتر", "مشتريات مستلمة غير مفوترة", "goods received not invoiced", "grni" }),
    };

    public static IEnumerable<string> RoleKeys => Specs.Select(s => s.Role);

    private static PostingAccounts.Role RoleOf(Spec spec) => PostingAccounts.Roles.First(r => r.Key == spec.Role);

    // ---------- منشأة جديدة ----------

    /// <summary>
    /// عند إنشاء المنشأة (بلا سياق منشأة للاستعلام): يخطط على الشجرة الافتراضية المُضافة للتو ويضيف الحسابات والإعدادات بمعرّف المنشأة.
    /// </summary>
    public static void Provision(ErpDbContext db, Guid tenantId, string currency, IEnumerable<Node> chart)
    {
        var settings = new AccountingSettings { TenantId = tenantId };
        var (report, created) = Plan(chart.ToList(), settings, new HashSet<string>(), new Dictionary<string, string?>());
        foreach (var node in created) db.Add(ToAccount(node, currency, tenantId));
        if (report.Items.Any(i => i.Status is ContractAccountSetupStatus.Created or ContractAccountSetupStatus.Reused)) db.Add(settings);
    }

    // ---------- منشأة قائمة ----------

    /// <summary>للفوترة: لا شيء إن كانت إعدادات العقود كلها محددة (بلا تحميل الشجرة)، وإلا تهيئة الناقص.</summary>
    public static async Task EnsureMissingAsync(ErpDbContext db, CancellationToken ct)
    {
        var settings = await db.Set<AccountingSettings>().AsNoTracking().FirstOrDefaultAsync(ct);
        if (settings != null && Specs.All(s => RoleOf(s).Get(settings) != null)) return;
        await EnsureAsync(db, ct);
    }

    /// <summary>تهيئة المنشأة الحالية: تحفظ الحسابات الجديدة والإعدادات المربوطة (في معاملة المستدعي) وتعيد التقرير.</summary>
    public static async Task<ContractAccountsSetupDto> EnsureAsync(ErpDbContext db, CancellationToken ct)
    {
        var settings = await db.Set<AccountingSettings>().FirstOrDefaultAsync(ct);
        var isNewSettings = settings == null;
        settings ??= new AccountingSettings();

        var chart = await db.Set<Account>().AsNoTracking()
            .Select(a => new Node { Code = a.Code, NameAr = a.NameAr, NameEn = a.NameEn, Type = a.Type, ParentCode = a.ParentCode, Level = a.Level, Linked = a.LinkedEntityType })
            .ToListAsync(ct);
        var showroom = await AccountRoleRules.ShowroomAccountsAsync(db, ct);

        // الإعدادات المحددة: تُفحص بقواعد الأدوار نفسها؛ غير الصالح يُبلَّغ عنه ولا يُستبدل
        var existingProblems = new Dictionary<string, string?>();
        foreach (var spec in Specs)
        {
            var role = RoleOf(spec);
            if (role.Get(settings) is not { } code) continue;
            var problems = await AccountRoleRules.ProblemsAsync(db, new[] { (role, code) }, ct);
            existingProblems[spec.Role] = problems.FirstOrDefault();
        }

        var (report, created) = Plan(chart, settings, showroom, existingProblems);
        if (created.Count > 0)
        {
            var currency = await db.Set<Currency>().Where(c => c.IsBaseCurrency).Select(c => c.Code).FirstOrDefaultAsync(ct) ?? "SAR";
            foreach (var node in created) db.Add(ToAccount(node, currency, tenantId: null));
        }
        var linked = report.Items.Any(i => i.Status is ContractAccountSetupStatus.Created or ContractAccountSetupStatus.Reused);
        if (linked && isNewSettings) db.Add(settings);
        if (linked) await db.SaveChangesAsync(ct);
        return report;
    }

    // ---------- التخطيط ----------

    /// <summary>
    /// يقرر لكل دور: يبقى، يُعاد استخدام حساب، يُنشأ، أو مشكلة. يكتب الربط في <paramref name="settings"/> ويضيف المُنشأ إلى <paramref name="chart"/>.
    /// </summary>
    private static (ContractAccountsSetupDto Report, List<Node> Created) Plan(List<Node> chart, AccountingSettings settings,
        HashSet<string> excluded, Dictionary<string, string?> existingProblems)
    {
        var report = new ContractAccountsSetupDto();
        var created = new List<Node>();
        var byCode = chart.ToDictionary(n => n.Code);
        var parents = chart.Select(n => n.ParentCode).OfType<string>().ToHashSet();

        foreach (var spec in Specs)
        {
            var role = RoleOf(spec);
            var item = new ContractAccountSetupItemDto { Role = spec.Role, RoleLabel = role.Label };
            report.Items.Add(item);

            if (role.Get(settings) is { } current)
            {
                item.AccountCode = current;
                item.AccountName = byCode.TryGetValue(current, out var n) ? n.NameAr : null;
                if (existingProblems.GetValueOrDefault(spec.Role) is { } problem) { item.Status = ContractAccountSetupStatus.Problem; item.Message = problem; }
                else item.Status = ContractAccountSetupStatus.Kept;
                continue;
            }

            // حسابات أدوار أخرى (الإعدادات الحالية بعد ربط ما سبق في هذا التشغيل) لا تُستعار
            var taken = PostingAccounts.Roles.Select(r => r.Effective(settings)).OfType<string>().ToHashSet();
            var names = spec.Aliases.Append(spec.NameAr).Append(spec.NameEn).Select(Normalize).ToHashSet();
            var candidates = chart.Where(a => (names.Contains(Normalize(a.NameAr)) || names.Contains(Normalize(a.NameEn)))
                    && role.Types.Contains(a.Type) && !parents.Contains(a.Code)
                    && a.Linked is null && !IsUnder(a.Code, DefaultAccounts.Banks, byCode)
                    && !excluded.Contains(a.Code) && !taken.Contains(a.Code))
                .ToList();
            if (candidates.Count > 1)
            {
                item.Status = ContractAccountSetupStatus.Problem;
                item.Message = string.Format(Messages.ContractAccountAmbiguous, role.Label, string.Join("، ", candidates.Select(c => c.Code)));
                continue;
            }
            if (candidates.Count == 1)
            {
                var reuse = candidates[0];
                role.Set(settings, reuse.Code);
                item.Status = ContractAccountSetupStatus.Reused; item.AccountCode = reuse.Code; item.AccountName = reuse.NameAr;
                continue;
            }

            var parent = ParentFor(spec, settings, byCode);
            if (parent == null)
            {
                item.Status = ContractAccountSetupStatus.Problem;
                item.Message = string.Format(Messages.ContractAccountNoParent, role.Label);
                continue;
            }
            var node = new Node
            {
                Code = NextCode(parent, chart, byCode), NameAr = spec.NameAr, NameEn = spec.NameEn, Type = spec.Type,
                ParentCode = parent.Code, Level = parent.Level + 1, IsNew = true,
            };
            chart.Add(node); byCode[node.Code] = node; parents.Add(parent.Code);
            created.Add(node);
            role.Set(settings, node.Code);
            item.Status = ContractAccountSetupStatus.Created; item.AccountCode = node.Code; item.AccountName = node.NameAr;
        }
        return (report, created);
    }

    /// <summary>
    /// مجموعة الحساب: أب أول مرسى موجود من النوع نفسه (حيث تضع المنشأة حساباتها المماثلة فعلاً)، وإلا جذر النوع.
    /// الأب حساب تجميعي لا طرف (مجموعة العملاء/الموردين لا تُستخدم).
    /// </summary>
    private static Node? ParentFor(Spec spec, AccountingSettings settings, Dictionary<string, Node> byCode)
    {
        foreach (var anchor in spec.Anchors(settings).OfType<string>())
        {
            if (!byCode.TryGetValue(anchor, out var a) || a.Type != spec.Type || a.ParentCode == null) continue;
            if (byCode.TryGetValue(a.ParentCode, out var p) && p.Type == spec.Type && p.Linked is null) return p;
        }
        return byCode.Values.Where(n => n.ParentCode == null && n.Type == spec.Type).OrderBy(n => n.Code.Length).ThenBy(n => n.Code).FirstOrDefault();
    }

    /// <summary>
    /// رقم الحساب التالي في مجموعته: بعد أكبر رقم بين إخوته وبطوله نفسه. يبقى ضمن بادئة الأب إن كان الإخوة كلهم عليها
    /// (فإن امتلأت تُمدّ الخانة)؛ وإن كانت الشجرة قد تجاوزت البادئة من قبل (530 تحت 52) يتبع تسلسلها.
    /// </summary>
    private static string NextCode(Node parent, List<Node> chart, Dictionary<string, Node> byCode)
    {
        var siblings = chart.Where(n => n.ParentCode == parent.Code && n.Code.All(char.IsDigit)).Select(n => n.Code).ToList();
        if (siblings.Count > 0)
        {
            var width = siblings.GroupBy(c => c.Length).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
            var sameWidth = siblings.Where(c => c.Length == width).ToList();
            var prefixed = siblings.All(c => c.StartsWith(parent.Code, StringComparison.Ordinal));
            for (var next = sameWidth.Max(long.Parse) + 1; next.ToString().Length == width; next++)
            {
                var code = next.ToString();
                if (byCode.ContainsKey(code)) continue;
                if (!prefixed || code.StartsWith(parent.Code, StringComparison.Ordinal)) return code;
                break; // انتهت أرقام البادئة بهذا الطول
            }
        }
        // لا إخوة أو امتلأت البادئة: خانة إضافية بعد رقم الأب (أطول من الإخوة إن وُجدوا)
        var minWidth = Math.Max(parent.Code.Length + 1, siblings.Count > 0 ? siblings.Max(c => c.Length) + 1 : 0);
        for (var width = minWidth; ; width++)
        {
            var digits = width - parent.Code.Length;
            var limit = (long)Math.Pow(10, digits);
            for (long n = 1; n < limit; n++)
            {
                var code = parent.Code + n.ToString().PadLeft(digits, '0');
                if (!byCode.ContainsKey(code)) return code;
            }
        }
    }

    private static bool IsUnder(string code, string root, Dictionary<string, Node> byCode)
    {
        for (var c = code; c != null; c = byCode.TryGetValue(c, out var a) ? a.ParentCode : null)
            if (c == root) return true;
        return false;
    }

    /// <summary>مطابقة الأسماء: بلا فروق المسافات والحالة والهمزات والتاء المربوطة والألف المقصورة والتشكيل.</summary>
    private static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var chars = name.Trim().ToLowerInvariant()
            .Where(ch => ch is < 'ً' or > 'ْ') // التشكيل
            .Select(ch => ch switch { 'أ' or 'إ' or 'آ' => 'ا', 'ة' => 'ه', 'ى' => 'ي', _ => ch });
        return string.Join(' ', new string(chars.ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static Account ToAccount(Node n, string currency, Guid? tenantId)
    {
        var account = new Account
        {
            Code = n.Code, NameAr = n.NameAr, NameEn = n.NameEn, Type = n.Type, ParentCode = n.ParentCode, Level = n.Level,
            IsDebitNature = DefaultAccounts.IsDebitNature(n.Code, n.Type), IsSystem = true, Currency = currency,
        };
        if (tenantId is { } id) account.TenantId = id;
        return account;
    }
}
