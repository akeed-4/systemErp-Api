using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>دليل المنشأة الحالية من القوالب (شاشة إعدادات الحسابات): الحالة والمعاينة والتطبيق المعتمد والربط الوظيفي.</summary>
public class ChartSetupService : IChartSetupService
{
    private readonly ErpDbContext _db;
    private readonly ITransactionRunner _tx;
    private readonly ISubscriptionEntitlements _entitlements;
    private readonly IAuditService _audit;

    public ChartSetupService(ErpDbContext db, ITransactionRunner tx, ISubscriptionEntitlements entitlements, IAuditService audit)
    {
        _db = db; _tx = tx; _entitlements = entitlements; _audit = audit;
    }

    private async Task<string[]> LicensedAsync(CancellationToken ct) => (await _entitlements.GetAsync(ct)).Modules;

    public async Task<ChartSetupStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        var log = await _db.Set<ChartProvisioning>().AsNoTracking().FirstOrDefaultAsync(ct);
        var tenantActivity = await _db.Set<Tenant>().Select(t => t.ActivityKey).FirstOrDefaultAsync(ct);
        var licensed = (await LicensedAsync(ct)).Where(PlatformModules.Licensable.Contains).ToList();
        var applied = ChartTemplateCatalog.ParseCsv(log?.AppliedModules).ToList();
        return new ChartSetupStatusDto
        {
            IsLegacy = log == null,
            ActivityKey = log?.ActivityKey ?? tenantActivity,
            Status = log?.Status,
            LastError = log?.LastError,
            CompletedAt = log?.CompletedAt,
            AppliedTemplates = ChartTemplateCatalog.ParseCsv(log?.AppliedTemplates).ToList(),
            AppliedModules = applied,
            LicensedModules = licensed,
            PendingModules = log == null ? new List<string>() : licensed.Except(applied).ToList(),
        };
    }

    public async Task<ChartSetupReportDto> PreviewAsync(ApplyChartSetupDto request, CancellationToken ct = default)
        => await ChartSetupRunner.RunAsync(_db, await LicensedAsync(ct), request.ActivityKey, request.IncludeActivity, preview: true, ct);

    public Task<ChartSetupReportDto> ApplyAsync(ApplyChartSetupDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var report = await ChartSetupRunner.RunAsync(_db, await LicensedAsync(token), request.ActivityKey, request.IncludeActivity, preview: false, token);
            if (!report.Complete)
                throw new ValidationFailedException(Messages.ChartSetupIncomplete, report.Errors.Concat(report.Items
                    .Where(i => i.Status == ChartSetupItemStatus.Problem && i.Message != null).Select(i => i.Message!)).ToList());
            if (report.Created > 0 || report.Bound > 0 || request.IncludeActivity)
                await _audit.LogAsync("CHART_TEMPLATES_APPLIED", nameof(ChartProvisioning), request.ActivityKey ?? "modules",
                    $"تطبيق قوالب الدليل ({string.Join("، ", report.Templates)}): أُنشئ {report.Created} وربط {report.Bound} — "
                    + string.Join("، ", report.Items.Where(i => i.Status == ChartSetupItemStatus.Created).Select(i => $"{i.Code} {i.NameAr}")), token);
            return report;
        }, ct);

    public async Task<List<ChartSetupItemDto>> UnusedModuleAccountsAsync(CancellationToken ct = default)
    {
        var log = await _db.Set<ChartProvisioning>().AsNoTracking().FirstOrDefaultAsync(ct);
        if (log == null) return new List<ChartSetupItemDto>();
        var licensed = await LicensedAsync(ct);
        var dropped = ChartTemplateCatalog.ParseCsv(log.AppliedModules).Except(licensed).ToArray();
        if (dropped.Length == 0) return new List<ChartSetupItemDto>();

        var all = await ChartTemplateCatalog.LoadAsync(_db.Catalog, ct);
        var droppedTemplates = await ChartTemplateCatalog.SelectAsync(_db.Catalog, all, false, null, dropped, ct);
        // الحساب الذي يطلبه قالب ما زال سارياً (الأساسي، النشاط، موديول مرخّص) لا يُعرض
        var kept = await ChartTemplateCatalog.SelectAsync(_db.Catalog, all, true, log.ActivityKey, licensed, ct);
        var keptCodes = kept.SelectMany(t => t.Accounts).Select(a => a.Code).OfType<string>().ToHashSet();
        var keptKeys = kept.SelectMany(t => t.Accounts).Select(a => a.SemanticKey).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);

        var settings = await PostingAccounts.SettingsAsync(_db, ct);
        var bindings = await _db.Set<AccountBinding>().AsNoTracking().ToListAsync(ct);
        var accounts = await _db.Set<Account>().AsNoTracking().ToListAsync(ct);
        var byId = accounts.ToDictionary(a => a.Id);
        string? CodeOf(ChartTemplateAccount a)
        {
            if (a.SemanticKey == null) return a.Code;
            var def = AccountSemanticKeys.Of(a.SemanticKey);
            return def.Store switch
            {
                AccountSemanticKeys.SettingStore => def.Role!.Get(settings) ?? a.Code,
                AccountSemanticKeys.BindingStore => bindings.FirstOrDefault(b => string.Equals(b.SemanticKey, def.Key, StringComparison.OrdinalIgnoreCase)) is { } b
                    && byId.TryGetValue(b.AccountId, out var acc) ? acc.Code : a.Code,
                _ => a.Code,
            };
        }
        var candidates = droppedTemplates.SelectMany(t => t.Accounts.Select(a => (Template: t.Key, Account: a)))
            .Where(x => !(x.Account.SemanticKey != null && keptKeys.Contains(x.Account.SemanticKey)))
            .Select(x => (x.Template, Code: CodeOf(x.Account)))
            .Where(x => x.Code != null && !keptCodes.Contains(x.Code))
            .GroupBy(x => x.Code!).ToDictionary(g => g.Key, g => g.Select(x => x.Template).Distinct().ToList());
        if (candidates.Count == 0) return new List<ChartSetupItemDto>();

        var codes = candidates.Keys.ToList();
        var withEntries = (await _db.Set<JournalEntryLine>().AsNoTracking().Where(l => codes.Contains(l.AccountCode)).Select(l => l.AccountCode).Distinct().ToListAsync(ct)).ToHashSet();
        var linkedToMethods = (await _db.Set<PaymentMethodItem>().AsNoTracking().Where(p => codes.Contains(p.LinkedAccountCode)).Select(p => p.LinkedAccountCode).Distinct().ToListAsync(ct)).ToHashSet();
        var parents = accounts.Select(a => a.ParentCode).OfType<string>().ToHashSet();

        return accounts.Where(a => candidates.ContainsKey(a.Code) && a.IsActive && a.Balance == 0
                && !withEntries.Contains(a.Code) && !linkedToMethods.Contains(a.Code) && !parents.Contains(a.Code))
            .OrderBy(a => a.Code, StringComparer.Ordinal)
            .Select(a => new ChartSetupItemDto
            {
                Code = a.Code, NameAr = a.NameAr, NameEn = a.NameEn, ParentCode = a.ParentCode, Type = a.Type,
                Status = ChartSetupItemStatus.Existing, Message = Messages.ChartSetupUnusedCanDeactivate, Sources = candidates[a.Code],
            }).ToList();
    }

    public async Task<List<AccountBindingDto>> ListBindingsAsync(CancellationToken ct = default)
    {
        var settings = await PostingAccounts.SettingsAsync(_db, ct);
        var bindings = await _db.Set<AccountBinding>().AsNoTracking().ToListAsync(ct);
        var accounts = await _db.Set<Account>().AsNoTracking().Select(a => new { a.Id, a.Code, a.NameAr }).ToListAsync(ct);
        var byCode = accounts.ToDictionary(a => a.Code);
        var byId = accounts.ToDictionary(a => a.Id);

        var list = new List<AccountBindingDto>();
        foreach (var def in AccountSemanticKeys.All)
        {
            var code = def.Store == AccountSemanticKeys.SettingStore ? def.Role!.Effective(settings) : def.DefaultCode;
            list.Add(new AccountBindingDto
            {
                SemanticKey = def.Key, Label = def.Label, Store = def.Store, AccountCode = code,
                AccountName = code != null && byCode.TryGetValue(code, out var a) ? a.NameAr : null,
            });
        }
        foreach (var b in bindings.Where(b => AccountSemanticKeys.Of(b.SemanticKey).Store == AccountSemanticKeys.BindingStore))
        {
            byId.TryGetValue(b.AccountId, out var a);
            list.Add(new AccountBindingDto
            {
                SemanticKey = b.SemanticKey, Label = b.SemanticKey, Store = AccountSemanticKeys.BindingStore, AccountCode = a?.Code, AccountName = a?.NameAr,
            });
        }
        return list;
    }

    /// <summary>
    /// ربط مفتاح عام (binding) بحساب آخر. مفاتيح إعدادات الحسابات تُعدَّل من شاشتها (بقواعد أدوارها وقفلها بعد الاستخدام)،
    /// والمحجوزة يقرؤها النظام برقمها فلا تُربط بغيره.
    /// </summary>
    public Task<AccountBindingDto> SaveBindingAsync(SaveAccountBindingDto dto, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var key = AccountSemanticKeys.Normalize(dto.SemanticKey);
            if (!AccountSemanticKeys.IsValidFormat(key)) throw new ValidationFailedException(string.Format(Messages.ChartTemplateSemanticKeyInvalid, key));
            var def = AccountSemanticKeys.Of(key);
            if (def.Store == AccountSemanticKeys.SettingStore) throw new ValidationFailedException(Messages.AccountBindingUseSettings);
            if (def.Store == AccountSemanticKeys.ReservedStore) throw new ValidationFailedException(string.Format(Messages.AccountBindingReserved, def.DefaultCode));

            var account = await _db.Set<Account>().FirstOrDefaultAsync(a => a.Code == dto.AccountCode, token)
                ?? throw new NotFoundException(string.Format(Messages.AccountBindingAccountNotFound, dto.AccountCode));
            if (account.IsGroup || !account.IsActive) throw new ValidationFailedException(string.Format(Messages.AccountBindingNeedsPostingAccount, account.Code));

            var binding = await _db.Set<AccountBinding>().FirstOrDefaultAsync(b => b.SemanticKey == key, token);
            var previous = binding?.AccountId;
            if (binding == null) _db.Add(new AccountBinding { SemanticKey = key, AccountId = account.Id });
            else binding.AccountId = account.Id;
            await _db.SaveChangesAsync(token);
            AccountKeyResolver.Invalidate(_db.CurrentTenantId);
            await _audit.LogAsync("ACCOUNT_BINDING_CHANGED", nameof(AccountBinding), key,
                $"ربط {key} بالحساب {account.Code} {account.NameAr}" + (previous is { } p && p != account.Id ? " (بدل حساب سابق)" : ""), token);
            return new AccountBindingDto { SemanticKey = key, Label = key, Store = def.Store, AccountCode = account.Code, AccountName = account.NameAr };
        }, ct);
}
