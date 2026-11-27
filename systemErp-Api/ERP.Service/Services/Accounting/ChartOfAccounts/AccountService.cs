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
    // حذف حساب الكيان يحذف كيانه، وتغيير رمزه يغيّر رمزه على الكيان: معاً أو لا شيء
    protected override bool Transactional => true;

    protected override IQueryable<Account> ApplySearch(IQueryable<Account> q, string term)
        => q.Where(a => a.Code.Contains(term) || a.NameAr.Contains(term) || a.NameEn.Contains(term));

    protected override async Task ValidateAsync(CreateAccountDto dto, Account? existing, CancellationToken ct)
    {
        // حساب جديد مباشرة تحت البنوك (110) أو الصناديق (111): رمزه من تسلسل مجموعته الموحد (1103...، 1112...) داخل معاملة الإنشاء،
        // ولو أُرسل برمز (لا ترقيم يدوي في المجموعتين)
        if (existing == null && await TreasuryAccounts.IsNumberedGroupAsync(Db, dto.ParentCode, ct))
            dto.Code = await TreasuryAccounts.NextCodeAsync(Db, dto.ParentCode!, ct);

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
            // الحساب يُعرَّف تحت حساب رئيسي فقط (ومن له أبناء قائمون رئيسي فعلاً)
            if (!parent.IsGroup && !await Db.Set<Account>().AnyAsync(a => a.ParentCode == parent.Code, ct))
                throw new ValidationFailedException(Messages.ParentAccountMustBeGroup);
        }

        // رئيسي أو فرعي: الرئيسي تجميعي لا يُرحَّل عليه، والفرعي حساب حركة بلا أبناء
        var isGroup = dto.IsGroup ?? existing?.IsGroup ?? false;
        var linked = existing != null ? existing.LinkedEntityType : dto.LinkedEntityType;
        if (isGroup && linked is not null and not LinkedEntityType.General)
            throw new ValidationFailedException(Messages.LinkedAccountCannotBeGroup);
        if (existing != null)
        {
            if (!isGroup && await Db.Set<Account>().AnyAsync(a => a.ParentCode == existing.Code, ct))
                throw new ConflictException(Messages.AccountWithChildrenMustBeGroup);
            if (isGroup && !existing.IsGroup && await Db.Set<JournalEntryLine>().AnyAsync(l => l.AccountCode == existing.Code, ct))
                throw new ConflictException(Messages.AccountWithEntriesCannotBeGroup);
        }

        if (existing != null && existing.Code != dto.Code
            && await Db.Set<JournalEntryLine>().AnyAsync(l => l.AccountCode == existing.Code, ct))
            throw new ConflictException(Messages.CannotChangeCodeOfAccountWithEntries);
        // حساب عميل/مورد/بنك: تغيير رمزه أو أبيه يغيّر هوية الارتباط، فيُسمح به بلا تاريخ فقط وداخل مجموعة الكيان
        if (existing != null && (existing.Code != dto.Code || existing.ParentCode != dto.ParentCode)
            && await LinkedPartyAccounts.OwnerOfAsync(Db, existing, existing.Code, ct) is { } owner)
        {
            await LinkedPartyAccounts.EnsureAccountFreeAsync(Db, existing, owner, string.Format(Messages.LinkedAccountHasEntries, existing.Code), ct);
            var group = await LinkedPartyAccounts.GroupOfAsync(Db, owner.Type, ct);
            if (string.IsNullOrEmpty(dto.ParentCode) || !await IsWithinGroupAsync(dto.ParentCode, group, ct))
                throw new ValidationFailedException(string.Format(Messages.LinkedAccountCodeOutsideGroup, dto.Code, group));
        }
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
        // ربط حساب جديد بكيان قائم بلا حساب: يُحفظ رمزه على الكيان في المعاملة نفسها
        if (await LinkTargetAsync(dto, ct) is { } party) party.SetAccountCode(entity.Code);
        // حساب فرعي تحت العملاء أو الموردين أو البنوك: يُنشأ كيانه بالاسم نفسه في المعاملة نفسها (ما لم يُطلب «حساب عام»)
        else if (await AutoPartyTypeAsync(dto, ct) is { } type) await CreatePartyAsync(entity, type, dto, ct);
        // صندوق جديد تحت الصندوق/النقدية (111): طريقة دفع نقدية باسمه مرتبطة بحسابه
        else if (await CashBoxPaymentMethods.IsCashBoxAsync(Db, entity, ct)) await CashBoxPaymentMethods.EnsureForNewBoxAsync(Db, entity, ct);
    }

    /// <summary>
    /// نوع الكيان الذي يُنشأ تلقائياً للحساب الجديد من موقعه في الشجرة (سلسلة الآباء حتى مجموعة العملاء أو الموردين أو البنوك الفعلية)،
    /// أو null: حساب رئيسي، أو «عام» صراحةً، أو ربط بكيان قائم، أو خارج مجموعات الكيانات. نوع مُرسَل بلا كيان يجب أن يطابق الموقع.
    /// </summary>
    private async Task<LinkedEntityType?> AutoPartyTypeAsync(CreateAccountDto dto, CancellationToken ct)
    {
        if (dto.IsGroup == true || dto.LinkedEntityId != null || string.IsNullOrEmpty(dto.ParentCode)) return null;
        if (dto.LinkedEntityType is not (null or LinkedEntityType.Customer or LinkedEntityType.Supplier or LinkedEntityType.Bank)) return null;
        foreach (var type in new[] { LinkedEntityType.Customer, LinkedEntityType.Supplier, LinkedEntityType.Bank })
        {
            var group = await LinkedPartyAccounts.GroupOfAsync(Db, type, ct);
            if (!await IsWithinGroupAsync(dto.ParentCode, group, ct)) continue;
            if (dto.LinkedEntityType is { } requested && requested != type)
                throw new ValidationFailedException(string.Format(Messages.LinkedAccountCodeOutsideGroup, dto.Code, await LinkedPartyAccounts.GroupOfAsync(Db, requested, ct)));
            return type;
        }
        return dto.LinkedEntityType is null ? null
            : throw new ValidationFailedException(string.Format(Messages.LinkedAccountCodeOutsideGroup, dto.Code, await LinkedPartyAccounts.GroupOfAsync(Db, dto.LinkedEntityType.Value, ct)));
    }

    /// <summary>
    /// الكيان المقابل للحساب الجديد بالاسم نفسه ورمز من تسلسل نوعه، مربوطاً به بالمعرّف (LinkedEntityType/LinkedEntityId) ورمزه.
    /// الجوال والرقم الضريبي اختياريان هنا (يُكمَلان من شاشة الكيان)؛ عملة البنك عملة الحساب.
    /// </summary>
    private async Task CreatePartyAsync(Account account, LinkedEntityType type, CreateAccountDto dto, CancellationToken ct)
    {
        var nameAr = account.NameAr.Trim();
        var nameEn = string.IsNullOrWhiteSpace(account.NameEn) ? nameAr : account.NameEn.Trim();
        var phone = dto.EntityPhone?.Trim() ?? string.Empty;
        var vat = string.IsNullOrWhiteSpace(dto.EntityVatNumber) ? null : dto.EntityVatNumber.Trim();
        if (vat != null && !SaudiVat.IsValid(vat)) throw new ValidationFailedException(Messages.SaudiVatNumberFormat);
        var id = Guid.NewGuid();
        object party = type switch
        {
            LinkedEntityType.Customer => new Customer
            {
                Id = id, Code = await _numbers.NextAsync("customer", "CUST-", ct), NameAr = nameAr, NameEn = nameEn, Phone = phone,
                VatNumber = vat, IsVatRegistered = vat != null, AccountCode = account.Code,
            },
            LinkedEntityType.Supplier => new Supplier
            {
                Id = id, Code = await _numbers.NextAsync("supplier", "SUP-", ct), NameAr = nameAr, NameEn = nameEn, Phone = phone,
                VatNumber = vat, IsVatRegistered = vat != null, AccountCode = account.Code,
            },
            _ => new BankEntity
            {
                Id = id, Code = await _numbers.NextAsync("bank", "BNK-", ct), NameAr = nameAr, NameEn = nameEn, AccountCode = account.Code,
                Currency = string.IsNullOrWhiteSpace(account.Currency)
                    ? await Db.Set<Currency>().Where(c => c.IsBaseCurrency).Select(c => c.Code).FirstOrDefaultAsync(ct) ?? "SAR"
                    : account.Currency.Trim().ToUpperInvariant(),
            },
        };
        Db.Add(party);
        // الاسم أدخله المستخدم للحساب فيُفحص تكراره بين كيانات نوعه أيضاً
        await DuplicateEntityValidator.EnsureUniqueAsync(Db, party, ct);
        account.LinkedEntityType = type;
        account.LinkedEntityId = id;
    }

    /// <summary>
    /// حساب جديد يُربط بعميل/مورد/بنك قائم (شاشة الحسابات «ربط بكيان موجود»): الكيان موجود، وليس له حساب (لا مملوك ولا عام)،
    /// والحساب تحت مجموعة الكيان. الكيان الذي له حساب لا يُستبدل حسابه بحساب آخر من هنا.
    /// </summary>
    private async Task<LinkedPartyAccounts.Party?> LinkTargetAsync(CreateAccountDto dto, CancellationToken ct)
    {
        if (!LinkedPartyAccounts.IsParty(dto.LinkedEntityType)) return null;
        var type = dto.LinkedEntityType!.Value;
        if (dto.LinkedEntityId is not Guid id) return null;
        var party = await LinkedPartyAccounts.FindAsync(Db, type, id, ct) ?? throw new NotFoundException(string.Format(Messages.EntityNotFound, Label));
        var owned = await LinkedPartyAccounts.OwnedAccountAsync(Db, party, ct);
        if (owned != null || !string.IsNullOrWhiteSpace(party.AccountCode))
            throw new ConflictException(string.Format(Messages.PartyAlreadyHasAccount, party.Label, party.Name, owned?.Code ?? party.AccountCode));
        var group = await LinkedPartyAccounts.GroupOfAsync(Db, type, ct);
        if (string.IsNullOrEmpty(dto.ParentCode) || !await IsWithinGroupAsync(dto.ParentCode, group, ct))
            throw new ValidationFailedException(string.Format(Messages.LinkedAccountCodeOutsideGroup, dto.Code, group));
        return party;
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
        // رمز حساب الكيان تغيّر (بعد التحقق في ValidateAsync): يتبعه رمز الحساب على الكيان فلا ينقطع الارتباط
        var oldCode = original.GetValue<string>(nameof(Account.Code));
        var nameChanged = original.GetValue<string>(nameof(Account.NameAr)) != entity.NameAr || original.GetValue<string>(nameof(Account.NameEn)) != entity.NameEn;
        if ((oldCode != entity.Code || nameChanged) && await LinkedPartyAccounts.OwnerOfAsync(Db, entity, oldCode, ct) is { } owner)
        {
            if (oldCode != entity.Code) owner.SetAccountCode(entity.Code);
            // اسم الكيان المملوك يتبع اسم حسابه (والعكس في شاشة الكيان): الاسمان متطابقان دائماً
            if (nameChanged && !string.IsNullOrWhiteSpace(entity.NameAr))
            {
                owner.SetName(entity.NameAr.Trim(), entity.NameEn?.Trim());
                await DuplicateEntityValidator.EnsureUniqueAsync(Db, owner.Entity, ct);
            }
        }
        // طرق الدفع المرتبطة بالحساب (صندوق أو غيره): رمزه واسمه، وطريقة الصندوق التلقائية تتبع اسمه وحالته
        await CashBoxPaymentMethods.SyncAsync(Db, oldCode, original.GetValue<string>(nameof(Account.NameAr)), entity, ct);
        // الإعدادات المربوطة بالرقم القديم (أدوار الترحيل، حسابات المواقع، المعرض) تتبع الرقم الجديد
        if (oldCode != entity.Code) await AccountCodeReferences.RenameAsync(Db, oldCode, entity.Code, ct);
    }

    /// <summary>هل الحساب <paramref name="code"/> هو المجموعة <paramref name="group"/> أو أحد فروعها؟</summary>
    private async Task<bool> IsWithinGroupAsync(string code, string group, CancellationToken ct)
    {
        var parents = await Db.Set<Account>().AsNoTracking().Select(a => new { a.Code, a.ParentCode }).ToDictionaryAsync(a => a.Code, a => a.ParentCode, ct);
        var seen = new HashSet<string>();
        for (string? c = code; c != null && seen.Add(c); c = parents.GetValueOrDefault(c))
            if (c == group) return true;
        return false;
    }

    public async Task<string?> ChangeLinkedAccountCodeAsync(LinkedEntityType type, Guid entityId, string? currentCode, string? requestedCode, CancellationToken ct = default)
    {
        var requested = requestedCode?.Trim();
        if (string.IsNullOrEmpty(requested) || requested == currentCode) return currentCode;
        var party = await LinkedPartyAccounts.FindAsync(Db, type, entityId, ct) ?? throw new NotFoundException(string.Format(Messages.EntityNotFound, Label));
        var account = await LinkedPartyAccounts.OwnedAccountAsync(Db, party, ct)
            ?? throw new ConflictException(string.Format(Messages.LinkedAccountNotOwned, currentCode, party.Label));
        // كيان بلا رمز محفوظ وحسابه المرتبط به بالرمز المطلوب: يُثبَّت الارتباط القائم (لا تغيير في الحساب)
        if (account.Code == requested) return requested;
        await LinkedPartyAccounts.EnsureAccountFreeAsync(Db, account, party, string.Format(Messages.LinkedAccountHasEntries, account.Code), ct);

        var groupCode = await LinkedPartyAccounts.GroupOfAsync(Db, type, ct);
        var group = await Db.Set<Account>().FirstOrDefaultAsync(a => a.Code == groupCode, ct)
            ?? throw new ConflictException(string.Format(Messages.ParentAccountMissingInChart, groupCode));
        // الرمز الجديد حساب جديد (أرقام، غير مستخدم، تحت مجموعة الكيان): لا يُستبدل بحساب قائم آخر
        var parent = await RequestedLinkedParentAsync(requested, group, ct);
        account.Code = requested;
        account.ParentCode = parent.Code;
        account.Level = parent.Level + 1;
        account.Type = parent.Type;
        account.IsDebitNature = parent.IsDebitNature;
        account.LinkedEntityType = type;
        account.LinkedEntityId = entityId;
        parent.IsGroup = true;
        party.SetAccountCode(requested);
        // يُحفظ داخل معاملة التعديل: ما يليه (قيد الرصيد الافتتاحي) يبحث عن الحساب برمزه الجديد
        await Db.SaveChangesAsync(ct);
        return requested;
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
        // حساب عميل/مورد/بنك بلا حركات (لا قيود — فُحصت أعلاه — ولا مستندات أو سجلات تشير إليه أو إلى كيانه):
        // يُحذف مع كيانه في المعاملة نفسها، فلا يبقى كيان بلا حساب ولا حساب بلا كيان
        if (await LinkedPartyAccounts.OwnerOfAsync(Db, entity, entity.Code, ct) is { } owner)
        {
            if (owner.Type == LinkedEntityType.Customer && await Db.Set<InvoiceBase>().AnyAsync(i => i.PartyId == owner.Id, ct))
                throw new ConflictException(Messages.CannotDeleteCustomerWithInvoices);
            await LinkedPartyAccounts.EnsureAccountFreeAsync(Db, entity, owner, Messages.CannotDeleteAccountWithEntries, ct);
            await LinkedPartyAccounts.EnsurePartyUnreferencedAsync(Db, owner, ct);
            Db.Remove(owner.Entity);
            return;
        }
        // حساب يحمل رمزَه كيانٌ دون أن يملكه (مشترك بين أكثر من كيان): لا يُحذف فيبقى الكيان بلا حساب
        if (await Db.Set<Customer>().AnyAsync(c => c.AccountCode == entity.Code, ct)
            || await Db.Set<Supplier>().AnyAsync(s => s.AccountCode == entity.Code, ct)
            || await Db.Set<BankEntity>().AnyAsync(b => b.AccountCode == entity.Code, ct))
            throw new ConflictException(Messages.AccountLinkedToEntity);
        if (entity.LinkedEntityType is not null and not LinkedEntityType.General)
            throw new ConflictException(Messages.AccountLinkedToEntity);
        // الصندوق المحذوف (بلا قيود): تُحذف طريقة دفعه معه
        await CashBoxPaymentMethods.RemoveForAccountAsync(Db, entity.Code, ct);
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

    public async Task<string?> PeekTreasuryCodeAsync(string parentCode, CancellationToken ct = default)
        => await TreasuryAccounts.IsNumberedGroupAsync(Db, parentCode, ct) ? await TreasuryAccounts.PeekCodeAsync(Db, parentCode, ct) : null;

    public async Task<List<AccountDto>> GetTreeAsync(CancellationToken ct = default)
    {
        // ترتيب رقمي (الرموز أرقام فقط): الأقصر أولاً ثم بالقيمة، فيأتي 1109 قبل 11010 تحت أبيهما
        var all = (await Db.Set<Account>().AsNoTracking().OrderBy(a => a.Code.Length).ThenBy(a => a.Code).ToListAsync(ct)).Select(Mapper.Map<AccountDto>).ToList();
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
            // البنوك (110)، أو 111 قبل فصلها أو مع تعارض الرمز 110 (TreasuryAccounts)
            LinkedEntityType.Bank => await TreasuryAccounts.BanksGroupAsync(Db, ct),
            LinkedEntityType.Warehouse => DefaultAccounts.InventoryGroup,
            _ => throw new ValidationFailedException(Messages.EntityTypeNotSupportedForAutoAccount),
        };
        var group = await Db.Set<Account>().FirstOrDefaultAsync(a => a.Code == groupCode, ct)
            ?? throw new ConflictException(string.Format(Messages.ParentAccountMissingInChart, groupCode));

        Account parent;
        string code;
        // البنك تحت مجموعة البنوك: رمزه من تسلسلها دائماً، والرمز المطلوب من الشجرة يُتجاهل
        if (!string.IsNullOrWhiteSpace(requestedCode) && !await TreasuryAccounts.IsNumberedGroupAsync(Db, groupCode, ct))
        {
            // رمز اختاره المستخدم في شجرة الحسابات: يُحفظ كما عُرض له بعد التحقق، فلا يظهر في الشجرة برقم آخر
            code = requestedCode.Trim();
            parent = await RequestedLinkedParentAsync(code, group, ct);
        }
        else
        {
            // الرمز التالي من عدّاد المجموعة: لا يعيد رقم حساب محذوف ولا يتكرر بين طلبين متزامنين. البنوك على تسلسل مجموعتها
            // الموحد مع الافتراضيين (1101، 1102 ← 1103...)، والعملاء والموردون 3 خانات بعد رمز المجموعة (112001...).
            parent = group;
            code = await TreasuryAccounts.IsNumberedGroupAsync(Db, groupCode, ct)
                ? await TreasuryAccounts.NextCodeAsync(Db, groupCode, ct)
                : await HierarchicalCodes.NextAccountChildAsync(Db, groupCode, LinkedChildWidth, Messages.SubAccountCodesExhausted, ct);
        }

        var account = new Account
        {
            Code = code,
            NameAr = nameAr, NameEn = string.IsNullOrWhiteSpace(nameEn) ? nameAr : nameEn,
            Type = parent.Type, ParentCode = parent.Code, Level = parent.Level + 1,
            IsDebitNature = parent.IsDebitNature, Currency = parent.Currency,
            LinkedEntityType = type, LinkedEntityId = entityId,
        };
        parent.IsGroup = true;
        Db.Add(account);
        await Db.SaveChangesAsync(ct);
        return account.Code;
    }

    /// <summary>
    /// أب الرمز المطلوب لحساب كيان: أطول حساب قائم رمزه بادئة له، ويجب أن يكون مجموعة الكيان (112/211/110) أو أحد فروعها
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
                code = await HierarchicalCodes.NextAsync(Db, _numbers, HierarchicalCodes.AccountChildKey(parentCode, width), parentCode, width,
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
