using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// يطبّق ناتج دمج القوالب على دليل المنشأة الحالية (قاعدتها المشتركة أو المستقلة عبر سياقها): ينشئ الناقص فقط ويربط المفاتيح الفارغة
/// ويكتب حالة التأسيس، في حفظ واحد داخل معاملة المستدعي. لا يعدّل اسماً أو رقماً أو أباً لحساب قائم ولا يحذفه، ولا يمس قيداً.
/// التشغيل المتكرر لا يكرر شيئاً (الحساب يُعرف بمفتاحه المربوط ثم برقمه). أي خطأ دمج أو مشكلة في حساب إلزامي = لا كتابة إطلاقاً.
/// يُستدعى عند التأسيس وتفعيل موديول والتطبيق المعتمد فقط — لا من الفواتير ولا القيود ولا التقارير.
/// </summary>
internal static class ChartProvisioner
{
    public sealed class Request
    {
        public required List<ChartTemplate> Templates { get; init; }
        /// <summary>يُحفظ في سجل التأسيس (null = يبقى المحفوظ).</summary>
        public string? ActivityKey { get; init; }
        public bool SetActivity { get; init; }
        /// <summary>الموديولات التي تُعلَّم مطبَّقة بعد النجاح.</summary>
        public IEnumerable<string> Modules { get; init; } = Array.Empty<string>();
        public bool Preview { get; init; }
        /// <summary>عملة الحسابات المنشأة (منشأة جديدة لم تُحفظ عملتها بعد)؛ وإلا العملة الأساسية.</summary>
        public string? Currency { get; init; }
    }

    private sealed class Existing
    {
        public Guid Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public string NameAr { get; init; } = string.Empty;
        public string NameEn { get; init; } = string.Empty;
        public string? ParentCode { get; init; }
        public AccountCategory Type { get; init; }
        public int Level { get; init; }
        public bool IsGroup { get; init; }
    }

    public static async Task<ChartSetupReportDto> ApplyAsync(ErpDbContext db, Request request, CancellationToken ct)
    {
        if (!request.Preview) await LockAsync(db, ct);

        var chart = await db.Set<Account>().AsNoTracking()
            .Select(a => new Existing { Id = a.Id, Code = a.Code, NameAr = a.NameAr, NameEn = a.NameEn, ParentCode = a.ParentCode, Type = a.Type, Level = a.Level, IsGroup = a.IsGroup })
            .ToListAsync(ct);
        var byCode = chart.ToDictionary(a => a.Code);
        var byId = chart.ToDictionary(a => a.Id);
        // التخطيط على نسخ غير متتبَّعة: لا يُكتب شيء قبل التحقق من الاكتمال
        var settings = await PostingAccounts.SettingsAsync(db, ct);
        var bindings = await db.Set<AccountBinding>().AsNoTracking().ToListAsync(ct);
        var bindingByKey = bindings.GroupBy(b => b.SemanticKey, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        string? BoundCode(string key)
        {
            var def = AccountSemanticKeys.Of(key);
            switch (def.Store)
            {
                case AccountSemanticKeys.SettingStore:
                    var role = def.Role!;
                    var code = role.Get(settings);
                    if (code != null) return code;
                    return role.Default != null && byCode.ContainsKey(role.Default) ? role.Default : null;
                case AccountSemanticKeys.ReservedStore:
                    return def.DefaultCode != null && byCode.ContainsKey(def.DefaultCode) ? def.DefaultCode : null;
                default:
                    return bindingByKey.TryGetValue(key, out var b) && byId.TryGetValue(b.AccountId, out var acc) ? acc.Code : null;
            }
        }

        var existing = chart.Select(a => new ChartComposer.ExistingAccount
            { Code = a.Code, NameAr = a.NameAr, NameEn = a.NameEn, ParentCode = a.ParentCode, Type = a.Type, Level = a.Level, IsGroup = a.IsGroup }).ToList();
        var composed = ChartComposer.Compose(request.Templates, existing, BoundCode);

        var report = new ChartSetupReportDto { IsPreview = request.Preview, Templates = composed.Templates.ToList(), Errors = composed.Errors.ToList() };

        // حساب قائم فرعي عليه قيود لا يصير رئيسياً (المحرك يرفض الترحيل على حساب له فروع): ما تحته مشكلة
        var parentsWithEntries = composed.ExistingParentsToGroup.Count == 0 ? new HashSet<string>()
            : (await db.Set<JournalEntryLine>().AsNoTracking().Where(l => composed.ExistingParentsToGroup.Contains(l.AccountCode))
                .Select(l => l.AccountCode).Distinct().ToListAsync(ct)).ToHashSet();

        var currency = request.Currency
            ?? await db.Set<Currency>().Where(c => c.IsBaseCurrency).Select(c => c.Code).FirstOrDefaultAsync(ct) ?? "SAR";
        var problemCodes = new HashSet<string>();
        var newAccounts = new List<Account>();
        var newBindings = new List<(string Key, Guid? AccountId, Account? Created)>();
        var settingAssignments = new List<(PostingAccounts.Role Role, string Code)>();

        foreach (var row in composed.Rows.OrderBy(r => r.Level))
        {
            var item = new ChartSetupItemDto
            {
                Code = row.BoundTo ?? row.Code ?? string.Empty, NameAr = row.NameAr, NameEn = row.NameEn, ParentCode = row.ParentCode,
                Type = row.Type, SemanticKey = row.SemanticKey, IsRequired = row.IsRequired, Sources = row.Sources.ToList(),
            };
            report.Items.Add(item);

            if (row.BoundTo != null)
            {
                item.Status = ChartSetupItemStatus.Existing;
                if (byCode.TryGetValue(row.BoundTo, out var bound)) { item.NameAr = bound.NameAr; item.NameEn = bound.NameEn; item.ParentCode = bound.ParentCode; }
                continue;
            }
            if (row.Code == null) { item.Status = ChartSetupItemStatus.Problem; continue; } // سببه في أخطاء الدمج

            if (byCode.TryGetValue(row.Code, out var current))
            {
                if (current.Type != row.Type)
                {
                    item.Status = ChartSetupItemStatus.Problem;
                    item.Message = string.Format(Messages.ChartSetupCodeTakenByOtherType, row.Code, current.NameAr);
                    problemCodes.Add(row.Code);
                    continue;
                }
                item.NameAr = current.NameAr; item.NameEn = current.NameEn; item.ParentCode = current.ParentCode;
                item.Status = ChartSetupItemStatus.Existing;
                if (row.SemanticKey != null && Bind(row.SemanticKey, row.Code, null)) item.Status = ChartSetupItemStatus.Bound;
                continue;
            }

            if (row.ParentCode != null && (problemCodes.Contains(row.ParentCode) || parentsWithEntries.Contains(row.ParentCode)))
            {
                item.Status = ChartSetupItemStatus.Problem;
                item.Message = parentsWithEntries.Contains(row.ParentCode)
                    ? string.Format(Messages.ChartSetupParentHasEntries, row.ParentCode)
                    : string.Format(Messages.ChartSetupParentProblem, row.ParentCode);
                problemCodes.Add(row.Code);
                continue;
            }

            var account = new Account
            {
                Code = row.Code, NameAr = row.NameAr, NameEn = row.NameEn, Type = row.Type, ParentCode = row.ParentCode, Level = row.Level,
                IsDebitNature = DefaultAccounts.IsDebitNature(row.Code, row.Type),
                // الرقم المحجوز يبقى حساب نظام (لا يُغيَّر رقمه ولا نوعه)؛ غيره حساب عادي يعيد المستخدم ترقيمه قبل استخدامه
                IsSystem = ReservedAccountCodes.IsReserved(row.Code),
                IsGroup = row.IsGroup || DefaultAccounts.IsGroup(row.Code),
                Currency = currency,
            };
            newAccounts.Add(account);
            item.Status = ChartSetupItemStatus.Created;
            if (row.SemanticKey != null) Bind(row.SemanticKey, row.Code, account);
        }

        // يربط المفتاح الفارغ فقط (المحدد من المستخدم لا يُمس)؛ true = رُبط الآن
        bool Bind(string key, string code, Account? created)
        {
            var def = AccountSemanticKeys.Of(key);
            switch (def.Store)
            {
                case AccountSemanticKeys.SettingStore:
                    var role = def.Role!;
                    if (role.Get(settings) != null || role.Default == code) return false; // الافتراضي يبقى فارغاً كما في كل المنشآت
                    if (!role.Types.Contains(byCode.TryGetValue(code, out var e) ? e.Type : created!.Type)) return false;
                    role.Set(settings, code); // على النسخة غير المتتبَّعة: مفتاح آخر في التشغيل نفسه يرى الدور محجوزاً
                    settingAssignments.Add((role, code));
                    return true;
                case AccountSemanticKeys.ReservedStore:
                    return false; // يُقرأ برقمه
                default:
                    if (bindingByKey.ContainsKey(key)) return false;
                    Guid? accountId = created == null && byCode.TryGetValue(code, out var acc) ? acc.Id : null;
                    if (created == null && accountId == null) return false;
                    newBindings.Add((key, accountId, created));
                    bindingByKey[key] = new AccountBinding { SemanticKey = key, AccountId = accountId ?? Guid.Empty };
                    return true;
            }
        }

        if (!report.Complete || request.Preview) return report;

        db.AddRange(newAccounts);
        foreach (var (key, accountId, created) in newBindings)
            db.Add(new AccountBinding { SemanticKey = key, AccountId = accountId ?? created!.Id }); // معرّف الحساب الجديد يُولَّد عند إضافته
        if (settingAssignments.Count > 0)
        {
            var tracked = await db.Set<AccountingSettings>().FirstOrDefaultAsync(ct);
            if (tracked == null) { tracked = new AccountingSettings(); db.Add(tracked); }
            foreach (var (role, code) in settingAssignments)
                if (role.Get(tracked) == null) role.Set(tracked, code);
        }

        var toGroup = composed.ExistingParentsToGroup.Where(c => !parentsWithEntries.Contains(c)).ToList();
        if (toGroup.Count > 0)
            foreach (var parent in await db.Set<Account>().Where(a => toGroup.Contains(a.Code) && !a.IsGroup).ToListAsync(ct))
                parent.IsGroup = true;

        var log = await db.Set<ChartProvisioning>().FirstOrDefaultAsync(ct);
        if (log == null) { log = new ChartProvisioning(); db.Add(log); }
        if (request.SetActivity) log.ActivityKey = request.ActivityKey;
        log.AppliedTemplates = MergeCsv(log.AppliedTemplates, composed.Templates, byKeyPrefix: true);
        log.AppliedModules = MergeCsv(log.AppliedModules, request.Modules, byKeyPrefix: false);
        log.Status = ChartProvisioningStatus.Completed;
        log.LastError = null;
        log.AccountsCreated += report.Created;
        log.BindingsCreated += newBindings.Count + settingAssignments.Count;
        log.CompletedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        AccountKeyResolver.Invalidate(db.CurrentTenantId);
        return report;
    }

    /// <summary>حالة فشل خارج معاملة التطبيق (التي تراجعت): تُظهر المحاولة المتعثّرة لمدير المنصة وتُعاد من لوحته.</summary>
    public static async Task RecordFailureAsync(ErpDbContext db, string error, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var log = await db.Set<ChartProvisioning>().FirstOrDefaultAsync(ct);
        if (log == null) return; // منشأة قديمة: لا سجل يُنشأ لمحاولة فاشلة
        log.Status = ChartProvisioningStatus.Failed;
        log.LastError = error.Length > 1000 ? error[..1000] : error;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>القوالب بإصداراتها (المفتاح يُستبدل بإصداره الأحدث)، أو الموديولات (اتحاد).</summary>
    private static string MergeCsv(string current, IEnumerable<string> added, bool byKeyPrefix)
    {
        var items = ChartTemplateCatalog.ParseCsv(current).ToList();
        foreach (var value in added)
        {
            if (byKeyPrefix)
            {
                var key = value.Split('@')[0];
                items.RemoveAll(i => i.Split('@')[0] == key);
            }
            if (!items.Contains(value)) items.Add(value);
        }
        return string.Join(",", items);
    }

    /// <summary>طلبان متزامنان لتأسيس دليل المنشأة نفسها ينتظران بالدور (يُحرَّر القفل مع المعاملة)؛ الفهرس الفريد ضمان أخير.</summary>
    private static async Task LockAsync(ErpDbContext db, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction == null) return;
        var resource = $"chart:{db.CurrentTenantId:N}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DECLARE @r int; EXEC @r = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000; IF @r < 0 THROW 51000, 'chart provisioning lock timeout', 1;",
            ct);
    }
}
