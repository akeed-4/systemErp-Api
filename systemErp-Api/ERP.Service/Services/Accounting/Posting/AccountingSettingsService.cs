using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// إعدادات الحسابات المحاسبية وطريقة الجرد (تحديث جزئي). كل حساب يُتحقق منه قبل الحفظ: موجود، فرعي، ليس حساب طرف ولا نقدية/بنك
/// ولا حساباً من إعدادات معرض السيارات، ونوعه يناسب دوره، ولا يشترك حسابان من حسابات الميزانية (المخزون والضريبتان) في حساب واحد.
/// حسابات الميزانية لا تُغيَّر بعد أي حركة عليها، وطريقة الجرد لا تُغيَّر إلا قبل أول قيد أو مع بداية سنة مالية.
/// </summary>
public class AccountingSettingsService : IAccountingSettingsService
{
    /// <summary>الأدوار التي تتأثر حركاتها بطريقة الجرد: نشاطها بعد تاريخ السريان يمنع تغيير الطريقة أو إلغاءه.</summary>
    private static readonly string[] InventoryRoles =
    {
        nameof(AccountingSettings.InventoryAccountCode), nameof(AccountingSettings.CogsAccountCode),
        nameof(AccountingSettings.InventoryShortageAccountCode), nameof(AccountingSettings.InventorySurplusAccountCode),
        nameof(AccountingSettings.PurchasesAccountCode), nameof(AccountingSettings.PurchaseReturnsAccountCode),
        nameof(AccountingSettings.OpeningInventoryAccountCode), nameof(AccountingSettings.EndingInventoryAccountCode),
    };

    private readonly ErpDbContext _db;
    private readonly IPeriodLock _periods;
    private readonly ITransactionRunner _tx;
    private readonly IAuditService _audit;
    private readonly IAccountingPostingService _posting;
    private readonly IPermissionService _permissions;

    public AccountingSettingsService(ErpDbContext db, IPeriodLock periods, ITransactionRunner tx, IAuditService audit, IAccountingPostingService posting,
        IPermissionService permissions)
    {
        _db = db; _periods = periods; _tx = tx; _audit = audit; _posting = posting; _permissions = permissions;
    }

    public async Task<AccountingSettingsDto> GetAsync(CancellationToken ct = default)
        => ToDto(await PostingAccounts.SettingsAsync(_db, ct));

    public Task<AccountingSettingsDto> SaveAsync(SaveAccountingSettingsDto d, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var settings = await _db.Set<AccountingSettings>().FirstOrDefaultAsync(token);
            if (settings == null) { settings = new AccountingSettings(); _db.Add(settings); }
            var before = Mapper.Map<AccountingSettings>(settings);

            if (d.InventoryMethod is { } method)
            {
                if (!Enum.IsDefined(method)) throw new ValidationFailedException(Messages.InvalidInventoryMethod);
                settings.InventoryMethod = method;
            }
            var incoming = new AccountingSettings();
            Mapper.Apply(d, incoming);
            foreach (var role in PostingAccounts.Roles)
                if (role.Get(incoming) is { } value) role.Set(settings, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
            if (d.SalesReturnPeriodDays is { } salesDays) settings.SalesReturnPeriodDays = ReturnPeriod(salesDays);
            if (d.PurchaseReturnPeriodDays is { } purchaseDays) settings.PurchaseReturnPeriodDays = ReturnPeriod(purchaseDays);
            if (d.ConstructionSeparateApprover is { } separate) settings.ConstructionSeparateApprover = separate;

            // اختيار الدوري وحساباته الإلزامية ناقصة: تُهيّأ في المعاملة نفسها قبل التحقق (فشل الحفظ يلغي ما أُنشئ)
            ContractAccountsSetupDto? periodicSetup = null;
            if (settings.InventoryMethod == InventorySystem.Periodic && PostingAccounts.Roles.Any(r => r.RequiredForPeriodic && r.Get(settings) == null))
                periodicSetup = await ContractAccountsSetup.EnsurePeriodicAsync(_db, settings, token);

            await ValidateAccountsAsync(settings, token);
            await EnsureChangeableAsync(before, settings, token);
            if (settings.InventoryMethod != before.InventoryMethod) await ApplyMethodChangeAsync(before, settings, d.InventoryMethodEffectiveFrom, token);

            await _db.SaveChangesAsync(token);
            if (periodicSetup is { Created: > 0 } or { Reused: > 0 }) await LogPeriodicSetupAsync(periodicSetup, token);
            var dto = ToDto(settings);
            dto.PeriodicAccountsSetup = periodicSetup;
            return dto;
        }, ct);

    public Task<ContractAccountsSetupDto> InitializeContractAccountsAsync(CancellationToken ct = default)
        => _tx.RunAsync(token => ContractAccountsSetup.EnsureAsync(_db, token), ct);

    /// <summary>فحص اكتمال حسابات الجرد الدوري بلا أي كتابة (عند فتح الإعدادات).</summary>
    public Task<ContractAccountsSetupDto> GetPeriodicAccountsStatusAsync(CancellationToken ct = default)
        => ContractAccountsSetup.EnsurePeriodicAsync(_db, preview: true, ct);

    /// <summary>زر تهيئة حسابات الجرد الدوري: ينشئ الناقص ويربطه، ولا يغيّر الإعدادات المحددة (يبلّغ عن غير الصالح منها).</summary>
    public Task<ContractAccountsSetupDto> InitializePeriodicAccountsAsync(CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var report = await ContractAccountsSetup.EnsurePeriodicAsync(_db, preview: false, token);
            if (report.Created > 0 || report.Reused > 0) await LogPeriodicSetupAsync(report, token);
            return report;
        }, ct);

    public Task<PeriodicLegacyPreviewDto> GetPeriodicLegacyAsync(CancellationToken ct = default) => PeriodicLegacyRemediation.PreviewAsync(_db, ct);

    /// <summary>
    /// معالجة قيود الجرد الدوري القديمة للمنشأة الحالية: تُنشئ قيوداً، فتتطلب مع تعديل الإعدادات صلاحية ترحيل القيود؛ وتُسجَّل في التدقيق.
    /// </summary>
    public Task<PeriodicLegacyResultDto> ApplyPeriodicLegacyAsync(ApplyPeriodicLegacyDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            if (!await _permissions.HasPermissionAsync("accounts", ScreenAction.Post, token))
                throw new ForbiddenException(Messages.PeriodicLegacyPostForbidden);
            var result = await PeriodicLegacyRemediation.ApplyAsync(_db, _posting, request, token);
            await _audit.LogAsync("PERIODIC_LEGACY_RECLASSIFIED", nameof(AccountingSettings), "periodic",
                $"معالجة قيود الجرد الدوري القديمة: {result.Posted.Count} قيد بمبلغ {result.Posted.Sum(p => p.Amount):0.00} — "
                + string.Join("، ", result.Posted.Select(p => $"{p.LegacyEntryNumber} ← {p.EntryNumber} ({p.Date:yyyy-MM-dd})"))
                + (request.UseFirstOpenDateForLocked ? " — قيود الفترات المقفلة بأول يوم مفتوح" : ""), token);
            return result;
        }, ct);

    private Task LogPeriodicSetupAsync(ContractAccountsSetupDto report, CancellationToken ct)
        => _audit.LogAsync("PERIODIC_ACCOUNTS_INITIALIZED", nameof(AccountingSettings), "periodic",
            "تهيئة حسابات الجرد الدوري: " + string.Join("، ", report.Items
                .Where(i => i.Status is ContractAccountSetupStatus.Created or ContractAccountSetupStatus.Reused)
                .Select(i => $"{i.RoleLabel} {i.AccountCode} ({i.Status})")), ct);

    /// <summary>0 = بلا حد (يُحفظ فارغاً).</summary>
    private static int? ReturnPeriod(int days)
    {
        if (days is < 0 or > 3650) throw new ValidationFailedException(Messages.ReturnPeriodDaysInvalid);
        return days == 0 ? null : days;
    }

    private async Task ValidateAccountsAsync(AccountingSettings s, CancellationToken ct)
    {
        var errors = PostingAccounts.Roles
            .Where(r => r.Get(s) == null && r.RequiredForPeriodic && s.InventoryMethod == InventorySystem.Periodic)
            .Select(r => string.Format(Messages.AccountSettingRequiredForPeriodic, r.Label)).ToList();
        errors.AddRange(await AccountRoleRules.ProblemsAsync(_db,
            PostingAccounts.Roles.Where(r => r.Get(s) != null).Select(r => (r, r.Get(s)!)), ct));
        // حسابات الميزانية لكل منها حساب مستقل: ضريبتا المخرجات والمدخلات في حساب واحد تُفسدان الإقرار، والمخزون مع الضريبة يخلط الأرصدة
        foreach (var shared in PostingAccounts.Roles.Where(r => r.LockedAfterUse).GroupBy(r => r.Effective(s)).Where(g => g.Key != null && g.Count() > 1))
            errors.Add(string.Format(Messages.AccountSettingSharedByRoles, shared.Key, string.Join("، ", shared.Select(r => r.Label))));
        // ولا تقع ضريبة على حساب مخزون مستودع
        var warehouseAccounts = await PostingAccounts.WarehouseInventoryAccountsAsync(_db, ct);
        foreach (var vat in PostingAccounts.Roles.Where(r => r.LockedAfterUse && r.Key != PostingAccounts.InventoryRole.Key))
            if (vat.Effective(s) is { } code && warehouseAccounts.Contains(code))
                errors.Add(string.Format(Messages.AccountSettingSharedByRoles, code, $"{vat.Label}، {PostingAccounts.InventoryRole.Label}"));
        errors.AddRange(await PeriodicProblemsAsync(s, ct));
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
    }

    /// <summary>
    /// قواعد الجرد الدوري، تُطبَّق عند اختياره فقط (لا يتأثر حفظ إعدادات المستمر): المشتريات ومردوداتها وأول المدة وآخرها مصروفات داخل
    /// مجموعة تكلفة المبيعات، ولا يشترك حساب المشتريات أو المردودات مع أول المدة أو آخرها أو تكلفة البضاعة المباعة (احتساب مزدوج).
    /// أول المدة وآخرها في حساب واحد مسموح (حساب تغيّر المخزون): القيد الموحد صحيح به.
    /// </summary>
    private async Task<List<string>> PeriodicProblemsAsync(AccountingSettings s, CancellationToken ct)
    {
        var errors = new List<string>();
        if (s.InventoryMethod != InventorySystem.Periodic) return errors;

        var chart = await _db.Set<Account>().AsNoTracking().Select(a => new { a.Code, a.ParentCode, a.Type }).ToDictionaryAsync(a => a.Code, ct);
        string? ParentOf(string code) => chart.TryGetValue(code, out var a) ? a.ParentCode : null;
        var group = CostOfSales.Group(s, ParentOf);
        foreach (var role in PostingAccounts.Roles.Where(r => ContractAccountsSetup.PeriodicRoleKeys.Contains(r.Key)))
            if (role.Get(s) is { } code && chart.TryGetValue(code, out var account) && !CostOfSales.Contains(code, account.Type, ParentOf, group))
                errors.Add(string.Format(Messages.AccountSettingNotCostOfSales, role.Label, code));

        string Label(string key) => PostingAccounts.Roles.First(r => r.Key == key).Label;
        const string purchases = nameof(AccountingSettings.PurchasesAccountCode), returns = nameof(AccountingSettings.PurchaseReturnsAccountCode),
            opening = nameof(AccountingSettings.OpeningInventoryAccountCode), ending = nameof(AccountingSettings.EndingInventoryAccountCode),
            cogs = nameof(AccountingSettings.CogsAccountCode);
        var codes = new Dictionary<string, string?>
        {
            [purchases] = s.PurchasesAccountCode, [returns] = s.PurchaseReturnsAccountCode,
            [opening] = s.OpeningInventoryAccountCode, [ending] = s.EndingInventoryAccountCode, [cogs] = s.Cogs(),
        };
        if (codes[returns] != null && codes[returns] == codes[purchases])
            errors.Add(string.Format(Messages.PurchaseReturnsSameAsPurchases, codes[returns]));
        foreach (var (a, b) in new[] { (purchases, opening), (purchases, ending), (purchases, cogs), (returns, opening), (returns, ending), (returns, cogs),
                     (opening, cogs), (ending, cogs) })
            if (codes[a] is { } code && code == codes[b])
                errors.Add(string.Format(Messages.AccountSettingPeriodicOverlap, Label(a), Label(b), code));
        return errors;
    }

    /// <summary>حساب ميزانية عليه حركة لا يُبدَّل ولو صار رصيده صفراً (انظر PostingAccounts.Role.LockedAfterUse).</summary>
    private async Task EnsureChangeableAsync(AccountingSettings before, AccountingSettings after, CancellationToken ct)
    {
        foreach (var role in PostingAccounts.Roles.Where(r => r.LockedAfterUse))
        {
            var old = role.Effective(before);
            if (old == null || old == role.Effective(after)) continue;
            if (await _db.Set<JournalEntryLine>().AnyAsync(l => l.AccountCode == old, ct))
                throw new ConflictException(string.Format(Messages.AccountSettingHasMovements, role.Label, old));
        }
    }

    /// <summary>
    /// قبل أول قيد تسري الطريقة منذ البداية. بعده تسري من بداية سنة مالية مفتوحة لا نشاط مخزنياً فيها، وما قبلها يبقى على الطريقة
    /// السابقة؛ ولتغيير ثانٍ يجب أن تكون الدفاتر مقفلة قبل بداية الطريقة الحالية فلا يُعاد ترحيل ما سبقها.
    /// والعودة للطريقة السابقة قبل أي نشاط مخزني من تاريخ سريان التغيير إلغاءٌ له لا تغيير جديد.
    /// </summary>
    private async Task ApplyMethodChangeAsync(AccountingSettings before, AccountingSettings settings, DateTime? requestedFrom, CancellationToken ct)
    {
        if (!await _db.Set<JournalEntry>().AnyAsync(ct))
        {
            settings.InventoryMethodEffectiveFrom = null;
            settings.PreviousInventoryMethod = null;
            return;
        }
        if (before.InventoryMethodEffectiveFrom is { } pending && settings.InventoryMethod == before.PreviousInventoryMethod
            && !await HasInventoryActivityFromAsync(pending, before, settings, ct))
        {
            settings.InventoryMethodEffectiveFrom = null;
            settings.PreviousInventoryMethod = null;
            return;
        }

        var from = requestedFrom?.Date ?? throw new ValidationFailedException(Messages.InventoryMethodEffectiveFromRequired);
        var tenant = await _db.Set<Tenant>().AsNoTracking().FirstAsync(ct);
        if (!new[] { from.Year, from.Year + 1 }.Any(y => FiscalPeriodService.YearRange(tenant, y).From == from))
            throw new ValidationFailedException(Messages.InventoryMethodChangeAtYearStartOnly);
        await _periods.EnsureOpenAsync(from, ct);
        if (before.InventoryMethodEffectiveFrom is { } current && (from <= current || !(tenant.BooksLockedThrough >= current.AddDays(-1))))
            throw new ConflictException(Messages.InventoryMethodPriorPeriodOpen);
        if (await HasInventoryActivityFromAsync(from, before, settings, ct))
            throw new ConflictException(Messages.InventoryMethodChangeAfterMovements);

        settings.PreviousInventoryMethod = before.InventoryMethod;
        settings.InventoryMethodEffectiveFrom = from;
    }

    /// <summary>حركة مخزون، أو قيد على حساب يتأثر بطريقة الجرد (بإعداده السابق أو الجديد)، بتاريخ <paramref name="since"/> أو بعده.</summary>
    private async Task<bool> HasInventoryActivityFromAsync(DateTime since, AccountingSettings before, AccountingSettings after, CancellationToken ct)
    {
        if (await _db.Set<StockMovement>().AnyAsync(m => m.Date >= since, ct)) return true;
        var codes = PostingAccounts.Roles.Where(r => InventoryRoles.Contains(r.Key))
            .SelectMany(r => new[] { r.Effective(before), r.Effective(after) }).OfType<string>().Distinct().ToList();
        return await (from l in _db.Set<JournalEntryLine>()
                      join e in _db.Set<JournalEntry>() on l.JournalEntryId equals e.Id
                      where e.Date >= since && codes.Contains(l.AccountCode)
                      select l.Id).AnyAsync(ct);
    }

    private static AccountingSettingsDto ToDto(AccountingSettings s)
    {
        var dto = Mapper.Map<AccountingSettingsDto>(s);
        dto.ConstructionSeparateApprover = s.ConstructionSeparateApprover;
        dto.EffectiveAccounts = PostingAccounts.Roles.ToDictionary(r => r.Key, r => r.Effective(s));
        return dto;
    }
}
