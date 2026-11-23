using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

public class AccountService : CrudService<Account, AccountDto, CreateAccountDto, UpdateAccountDto>, IAccountService
{
    /// <summary>عرض تسلسل الحساب المرتبط بكيان تحت أبيه (122 ← 122001)، كحسابات العملاء والموردين؛ يمتد خانة إذا امتلأ.</summary>
    private const int LinkedChildWidth = 3;
    private const int LinkedChildMaxWidth = 6;

    private readonly INumberSequenceService _numbers;

    public AccountService(ErpDbContext db, INumberSequenceService numbers) : base(db) { _numbers = numbers; }

    protected override string Label => Messages.LabelAccount;

    protected override IQueryable<Account> ApplySearch(IQueryable<Account> q, string term)
        => q.Where(a => a.Code.Contains(term) || a.NameAr.Contains(term) || a.NameEn.Contains(term));

    protected override async Task ValidateAsync(CreateAccountDto dto, Account? existing, CancellationToken ct)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(dto.Code)) errors.Add(Messages.AccountCodeRequired);
        else if (!dto.Code.All(char.IsDigit)) errors.Add(Messages.AccountCodeDigitsOnly);
        if (string.IsNullOrWhiteSpace(dto.NameAr)) errors.Add(Messages.AccountArabicNameRequired);
        if (dto.CostCenterPolicy is { } policy && !Enum.IsDefined(policy)) errors.Add(Messages.CostCenterPolicyInvalid);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        if (await Db.Set<Account>().AnyAsync(a => a.Code == dto.Code && (existing == null || a.Id != existing.Id), ct))
            throw new ConflictException(Messages.AccountCodeInUse);

        if (!string.IsNullOrEmpty(dto.ParentCode))
        {
            var parent = await Db.Set<Account>().AsNoTracking().FirstOrDefaultAsync(a => a.Code == dto.ParentCode, ct)
                ?? throw new ValidationFailedException(Messages.ParentAccountNotFound);
            if (parent.Type != dto.Type) throw new ValidationFailedException(Messages.AccountTypeMustMatchParent);
            if (existing != null && dto.ParentCode == existing.Code) throw new ValidationFailedException(Messages.AccountCannotBeOwnParent);
        }

        if (existing != null && existing.Code != dto.Code
            && await Db.Set<JournalEntryLine>().AnyAsync(l => l.AccountCode == existing.Code, ct))
            throw new ConflictException(Messages.CannotChangeCodeOfAccountWithEntries);
        if (existing is { IsSystem: true } && (existing.Code != dto.Code || existing.Type != dto.Type))
            throw new ConflictException(Messages.CannotEditSystemAccountCodeOrType);
        // حسابات النظام تُرحِّل عليها المستندات آلياً (الضريبة، المخزون...): إيقافها يعطّل الترحيل كله
        if (existing is { IsSystem: true } && dto.IsActive == false)
            throw new ConflictException(Messages.CannotDeactivateSystemAccount);

        if (dto.DefaultCostCenterId is { } center)
        {
            var effectivePolicy = dto.CostCenterPolicy ?? existing?.CostCenterPolicy ?? CostCenterPolicy.Optional;
            if (effectivePolicy == CostCenterPolicy.None) throw new ValidationFailedException(Messages.DefaultCostCenterNotAllowedForAccount);
            // مركز جديد يُشترط نشاطه؛ المركز المحفوظ مسبقاً يبقى وإن أُوقف بعدها (الترحيل يرفضه هناك)
            await CostCenterRules.EnsureUsableAsync(Db, new Guid?[] { center }, ct, requireActive: existing?.DefaultCostCenterId != center);
        }
    }

    protected override async Task OnCreatingAsync(Account entity, CreateAccountDto dto, CancellationToken ct)
    {
        entity.Level = await LevelOfAsync(dto.ParentCode, ct);
        entity.IsSystem = false;
        entity.Balance = 0; // الأرصدة تتغيّر بالقيود فقط
    }

    protected override async Task OnUpdatingAsync(Account entity, UpdateAccountDto dto, CancellationToken ct)
    {
        entity.Level = await LevelOfAsync(dto.ParentCode, ct);
        // الأرصدة والارتباط بكيان لا تُعدَّل من هنا: تُعاد القيم الأصلية التي كتبها Mapper.Apply.
        var original = Db.Entry(entity).OriginalValues;
        entity.Balance = original.GetValue<decimal>(nameof(Account.Balance));
        entity.IsSystem = original.GetValue<bool>(nameof(Account.IsSystem));
        entity.LinkedEntityType = original.GetValue<LinkedEntityType?>(nameof(Account.LinkedEntityType));
        entity.LinkedEntityId = original.GetValue<Guid?>(nameof(Account.LinkedEntityId));
    }

    protected override async Task OnDeletingAsync(Account entity, CancellationToken ct)
    {
        if (entity.IsSystem) throw new ConflictException(Messages.CannotDeleteSystemAccount);
        if (await Db.Set<Account>().AnyAsync(a => a.ParentCode == entity.Code, ct))
            throw new ConflictException(Messages.CannotDeleteAccountWithChildren);
        if (await Db.Set<JournalEntryLine>().AnyAsync(l => l.AccountCode == entity.Code, ct))
            throw new ConflictException(Messages.CannotDeleteAccountWithEntries);
        if (entity.LinkedEntityType is LinkedEntityType.FixedAsset)
            throw new ConflictException(Messages.AccountLinkedToFixedAsset);
        if (entity.LinkedEntityType is not null and not LinkedEntityType.General)
            throw new ConflictException(Messages.AccountLinkedToEntity);
    }

    private async Task<int> LevelOfAsync(string? parentCode, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(parentCode)) return 1;
        return (await Db.Set<Account>().AsNoTracking().Where(a => a.Code == parentCode).Select(a => a.Level).FirstAsync(ct)) + 1;
    }

    public override async Task<PagedResult<AccountDto>> ListAsync(PaginationParams p, CancellationToken ct = default)
    {
        p.SortBy ??= nameof(Account.Code);
        if (p.SortBy == nameof(Account.Code) && p.IsDescending) p.IsDescending = false;
        return await base.ListAsync(p, ct);
    }

    protected override DevExtreme.AspNet.Data.SortingInfo[] DefaultLoadSort => new[] { EntityLoader.Asc(nameof(Account.Code)) };

    public async Task<AccountDto> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        var a = await Db.Set<Account>().AsNoTracking().FirstOrDefaultAsync(x => x.Code == code, ct)
            ?? throw new NotFoundException(Messages.AccountNotFound);
        return Mapper.Map<AccountDto>(a);
    }

    public async Task<List<AccountDto>> GetTreeAsync(CancellationToken ct = default)
    {
        var all = (await Db.Set<Account>().AsNoTracking().OrderBy(a => a.Code).ToListAsync(ct)).Select(Mapper.Map<AccountDto>).ToList();
        var byCode = all.ToDictionary(a => a.Code);
        var roots = new List<AccountDto>();
        foreach (var a in all)
        {
            if (!string.IsNullOrEmpty(a.ParentCode) && byCode.TryGetValue(a.ParentCode, out var parent))
                (parent.Children ??= new()).Add(a);
            else roots.Add(a);
        }
        decimal Roll(AccountDto a)
        {
            if (a.Children == null) return a.Balance;
            var sum = a.Children.Sum(Roll);
            a.Balance += sum; // رصيد الأب = رصيده المباشر + أبناؤه
            return a.Balance;
        }
        foreach (var r in roots) Roll(r);
        return roots;
    }

    public Task<string> EnsureLinkedAccountAsync(LinkedEntityType type, Guid entityId, string nameAr, string nameEn, CancellationToken ct = default)
        => EnsureLinkedAccountAsync(type, entityId, nameAr, nameEn, null, ct);

    public async Task<string> EnsureLinkedAccountAsync(LinkedEntityType type, Guid entityId, string nameAr, string nameEn, string? requestedCode, CancellationToken ct = default)
    {
        var existing = await Db.Set<Account>().FirstOrDefaultAsync(a => a.LinkedEntityType == type && a.LinkedEntityId == entityId, ct);
        if (existing != null) return existing.Code;

        var groupCode = type switch
        {
            LinkedEntityType.Customer => DefaultAccounts.Receivables,
            LinkedEntityType.Supplier => DefaultAccounts.Payables,
            LinkedEntityType.Bank => DefaultAccounts.Banks,
            LinkedEntityType.Warehouse => DefaultAccounts.InventoryGroup,
            _ => throw new ValidationFailedException(Messages.EntityTypeNotSupportedForAutoAccount),
        };
        var group = await Db.Set<Account>().FirstOrDefaultAsync(a => a.Code == groupCode, ct)
            ?? throw new ConflictException(string.Format(Messages.ParentAccountMissingInChart, groupCode));

        Account parent;
        string code;
        if (!string.IsNullOrWhiteSpace(requestedCode))
        {
            // رمز اختاره المستخدم في شجرة الحسابات: يُحفظ كما عُرض له بعد التحقق، فلا يظهر في الشجرة برقم آخر
            code = requestedCode.Trim();
            parent = await RequestedLinkedParentAsync(code, group, ct);
        }
        else
        {
            // أكبر كود فرعي حالي تحت الأب (خانات الأبناء: 3 أرقام بعد كود الأب)
            parent = group;
            var existingCodes = await Db.Set<Account>().AsNoTracking()
                .Where(a => a.ParentCode == groupCode && a.Code.Length == groupCode.Length + 3)
                .Select(a => a.Code).ToListAsync(ct);
            var next = existingCodes.Select(c => HierarchicalCodes.SequenceOf(c, groupCode, 3) ?? 0).DefaultIfEmpty(0).Max() + 1;
            if (next > 999) throw new ConflictException(Messages.SubAccountCodesExhausted);
            code = $"{groupCode}{next:D3}";
        }

        var account = new Account
        {
            Code = code,
            NameAr = nameAr, NameEn = string.IsNullOrWhiteSpace(nameEn) ? nameAr : nameEn,
            Type = parent.Type, ParentCode = parent.Code, Level = parent.Level + 1,
            IsDebitNature = parent.IsDebitNature, Currency = parent.Currency,
            LinkedEntityType = type, LinkedEntityId = entityId,
        };
        Db.Add(account);
        await Db.SaveChangesAsync(ct);
        return account.Code;
    }

    /// <summary>
    /// أب الرمز المطلوب لحساب كيان: أطول حساب قائم رمزه بادئة له، ويجب أن يكون مجموعة الكيان (112/211/111) أو أحد فروعها
    /// غير المرتبطة بكيان آخر؛ والرمز أرقام فقط وغير مستخدم.
    /// </summary>
    private async Task<Account> RequestedLinkedParentAsync(string code, Account group, CancellationToken ct)
    {
        if (!code.All(char.IsDigit)) throw new ValidationFailedException(Messages.AccountCodeDigitsOnly);
        if (await Db.Set<Account>().AnyAsync(a => a.Code == code, ct)) throw new ConflictException(Messages.AccountCodeInUse);

        var prefixes = Enumerable.Range(1, Math.Max(0, code.Length - 1)).Select(n => code[..n]).ToList();
        var parent = await Db.Set<Account>().Where(a => prefixes.Contains(a.Code)).OrderByDescending(a => a.Code.Length).FirstOrDefaultAsync(ct);
        var outside = string.Format(Messages.LinkedAccountCodeOutsideGroup, code, group.Code);
        if (parent == null || parent.LinkedEntityType is not (null or LinkedEntityType.General)) throw new ValidationFailedException(outside);

        var parents = await Db.Set<Account>().AsNoTracking().Select(a => new { a.Code, a.ParentCode }).ToDictionaryAsync(a => a.Code, a => a.ParentCode, ct);
        var seen = new HashSet<string>();
        for (string? c = parent.Code; c != null && seen.Add(c); c = parents.GetValueOrDefault(c))
            if (c == group.Code) return parent;
        throw new ValidationFailedException(outside);
    }

    public async Task<bool> CanHoldSubAccountsAsync(string code, CancellationToken ct = default)
    {
        var id = await Db.Set<Account>().Where(a => a.Code == code).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
        if (id == null) return false;
        if (await Db.Set<Account>().AnyAsync(a => a.ParentCode == code, ct)) return true; // تجميعي أصلاً
        if (await Db.Set<JournalEntryLine>().AnyAsync(l => l.AccountCode == code, ct)) return false;
        if (await Db.Set<FixedAsset>().AnyAsync(a => a.AssetAccountId == id || a.AccumulatedDepreciationAccountId == id || a.DepreciationExpenseAccountId == id, ct))
            return false;
        if (await Db.Set<PaymentMethodItem>().AnyAsync(p => p.LinkedAccountCode == code, ct)) return false;
        // أدوار إعدادات الحسابات (كل خصائص *AccountCode النصية)
        var settings = await Db.Set<AccountingSettings>().AsNoTracking().FirstOrDefaultAsync(ct);
        return settings == null || !typeof(AccountingSettings).GetProperties()
            .Where(p => p.PropertyType == typeof(string) && p.Name.EndsWith("AccountCode", StringComparison.Ordinal))
            .Any(p => (string?)p.GetValue(settings) == code);
    }

    public async Task<(Guid Id, string Code)> CreateLinkedChildAccountAsync(string parentCode, LinkedEntityType type, Guid entityId,
        string nameAr, string nameEn, CancellationToken ct = default)
    {
        var parent = await Db.Set<Account>().AsNoTracking().FirstOrDefaultAsync(a => a.Code == parentCode, ct)
            ?? throw new ConflictException(string.Format(Messages.ParentAccountMissingInChart, parentCode));

        // 3 خانات بعد رمز الأب كبقية الحسابات المرتبطة؛ إن امتلأت (999) تمتد خانة بعدّاد مستقل لكل عرض.
        string? code = null;
        for (var width = LinkedChildWidth; code == null; width++)
        {
            try
            {
                code = await HierarchicalCodes.NextAsync(Db, _numbers, $"account_child_code:{parentCode}:{width}", parentCode, width,
                    Db.Set<Account>().Select(a => a.Code), Messages.SubAccountCodesExhausted, ct);
            }
            catch (ConflictException) when (width < LinkedChildMaxWidth) { }
        }

        var account = new Account
        {
            Code = code,
            NameAr = nameAr, NameEn = string.IsNullOrWhiteSpace(nameEn) ? nameAr : nameEn,
            Type = parent.Type, ParentCode = parent.Code, Level = parent.Level + 1,
            IsDebitNature = parent.IsDebitNature, Currency = parent.Currency,
            CostCenterPolicy = parent.CostCenterPolicy,
            DefaultCostCenterId = parent.CostCenterPolicy == CostCenterPolicy.None ? null : parent.DefaultCostCenterId,
            LinkedEntityType = type, LinkedEntityId = entityId,
        };
        Db.Add(account);
        await Db.SaveChangesAsync(ct);
        return (account.Id, account.Code);
    }
}
