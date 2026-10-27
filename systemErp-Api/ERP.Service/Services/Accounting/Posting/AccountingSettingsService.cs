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

    public AccountingSettingsService(ErpDbContext db, IPeriodLock periods, ITransactionRunner tx)
    {
        _db = db; _periods = periods; _tx = tx;
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

            await ValidateAccountsAsync(settings, token);
            await EnsureChangeableAsync(before, settings, token);
            if (settings.InventoryMethod != before.InventoryMethod) await ApplyMethodChangeAsync(before, settings, d.InventoryMethodEffectiveFrom, token);

            await _db.SaveChangesAsync(token);
            return ToDto(settings);
        }, ct);

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
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
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
        dto.EffectiveAccounts = PostingAccounts.Roles.ToDictionary(r => r.Key, r => r.Effective(s));
        return dto;
    }
}
