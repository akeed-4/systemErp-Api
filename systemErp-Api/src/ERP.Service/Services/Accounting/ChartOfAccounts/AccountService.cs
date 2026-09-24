using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

public class AccountService : CrudService<Account, AccountDto, CreateAccountDto, UpdateAccountDto>, IAccountService
{
    public AccountService(ErpDbContext db) : base(db) { }

    protected override string Label => "الحساب";

    protected override IQueryable<Account> ApplySearch(IQueryable<Account> q, string term)
        => q.Where(a => a.Code.Contains(term) || a.NameAr.Contains(term) || a.NameEn.Contains(term));

    protected override async Task ValidateAsync(CreateAccountDto dto, Account? existing, CancellationToken ct)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(dto.Code)) errors.Add("كود الحساب مطلوب.");
        else if (!dto.Code.All(char.IsDigit)) errors.Add("كود الحساب أرقام فقط.");
        if (string.IsNullOrWhiteSpace(dto.NameAr)) errors.Add("اسم الحساب بالعربية مطلوب.");
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        if (await Db.Set<Account>().AnyAsync(a => a.Code == dto.Code && (existing == null || a.Id != existing.Id), ct))
            throw new ConflictException("كود الحساب مستخدم مسبقاً.");

        if (!string.IsNullOrEmpty(dto.ParentCode))
        {
            var parent = await Db.Set<Account>().AsNoTracking().FirstOrDefaultAsync(a => a.Code == dto.ParentCode, ct)
                ?? throw new ValidationFailedException("الحساب الأب غير موجود.");
            if (parent.Type != dto.Type) throw new ValidationFailedException("نوع الحساب يجب أن يطابق نوع الحساب الأب.");
            if (existing != null && dto.ParentCode == existing.Code) throw new ValidationFailedException("لا يمكن أن يكون الحساب أباً لنفسه.");
        }

        if (existing != null && existing.Code != dto.Code
            && await Db.Set<JournalEntryLine>().AnyAsync(l => l.AccountCode == existing.Code, ct))
            throw new ConflictException("لا يمكن تغيير كود حساب عليه حركات.");
        if (existing is { IsSystem: true } && (existing.Code != dto.Code || existing.Type != dto.Type))
            throw new ConflictException("لا يمكن تعديل كود أو نوع حساب نظامي.");
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
        if (entity.IsSystem) throw new ConflictException("لا يمكن حذف حساب نظامي.");
        if (await Db.Set<Account>().AnyAsync(a => a.ParentCode == entity.Code, ct))
            throw new ConflictException("لا يمكن حذف حساب له حسابات فرعية.");
        if (await Db.Set<JournalEntryLine>().AnyAsync(l => l.AccountCode == entity.Code, ct))
            throw new ConflictException("لا يمكن حذف حساب عليه حركات.");
        if (entity.LinkedEntityType is not null and not LinkedEntityType.General)
            throw new ConflictException("الحساب مرتبط بعميل/مورد/بنك؛ احذف الكيان المرتبط أولاً.");
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

    public async Task<AccountDto> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        var a = await Db.Set<Account>().AsNoTracking().FirstOrDefaultAsync(x => x.Code == code, ct)
            ?? throw new NotFoundException("الحساب غير موجود");
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

    public async Task<string> EnsureLinkedAccountAsync(LinkedEntityType type, Guid entityId, string nameAr, string nameEn, CancellationToken ct = default)
    {
        var existing = await Db.Set<Account>().FirstOrDefaultAsync(a => a.LinkedEntityType == type && a.LinkedEntityId == entityId, ct);
        if (existing != null) return existing.Code;

        var parentCode = type switch
        {
            LinkedEntityType.Customer => DefaultAccounts.Receivables,
            LinkedEntityType.Supplier => DefaultAccounts.Payables,
            LinkedEntityType.Bank => DefaultAccounts.Banks,
            _ => throw new ValidationFailedException("نوع الكيان غير مدعوم لإنشاء حساب تلقائي."),
        };
        var parent = await Db.Set<Account>().FirstOrDefaultAsync(a => a.Code == parentCode, ct)
            ?? throw new ConflictException($"الحساب الأب {parentCode} غير موجود في شجرة الحسابات.");

        // أكبر كود فرعي حالي تحت الأب (خانات الأبناء: 3 أرقام بعد كود الأب)
        var existingCodes = await Db.Set<Account>().AsNoTracking()
            .Where(a => a.ParentCode == parentCode && a.Code.Length == parentCode.Length + 3)
            .Select(a => a.Code).ToListAsync(ct);
        var next = existingCodes.Select(c => int.Parse(c[parentCode.Length..])).DefaultIfEmpty(0).Max() + 1;
        if (next > 999) throw new ConflictException("تم استهلاك جميع الأكواد الفرعية المتاحة تحت هذا الحساب.");

        var account = new Account
        {
            Code = $"{parentCode}{next:D3}",
            NameAr = nameAr, NameEn = string.IsNullOrWhiteSpace(nameEn) ? nameAr : nameEn,
            Type = parent.Type, ParentCode = parentCode, Level = parent.Level + 1,
            IsDebitNature = parent.IsDebitNature, Currency = parent.Currency,
            LinkedEntityType = type, LinkedEntityId = entityId,
        };
        Db.Add(account);
        await Db.SaveChangesAsync(ct);
        return account.Code;
    }
}
