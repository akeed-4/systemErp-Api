using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

public class FixedAssetService : CrudService<FixedAsset, FixedAssetDto, CreateFixedAssetDto, UpdateFixedAssetDto>, IFixedAssetService
{
    private readonly IAuditService _audit;
    private readonly IAccountingPostingService _posting;
    private Guid? _costCenterFilter;
    private Guid? _warehouseFilter;
    private readonly IAccountService _accounts;
    private Guid? _previousCostCenterId;
    /// <summary>عند الإنشاء: لم يُحدَّد حساب مجمع الإهلاك/مصروفه، فيُنشأ للأصل حساب مخصص تحت الحساب الافتراضي.</summary>
    private bool _dedicateAccumulated, _dedicateExpense;
    private List<Account> _accountsToDelete = new();

    public FixedAssetService(ErpDbContext db, IAuditService audit, IAccountingPostingService posting, IAccountService accounts) : base(db)
    {
        _audit = audit; _posting = posting; _accounts = accounts;
    }
    protected override string Label => Messages.LabelFixedAsset;
    protected override bool Transactional => true; // الأصل وقيد اقتنائه معاً أو لا شيء

    public Task<PagedResult<FixedAssetDto>> ListAsync(Guid? costCenterId, Guid? warehouseId, PaginationParams p, CancellationToken ct = default)
    {
        _costCenterFilter = costCenterId;
        _warehouseFilter = warehouseId;
        return ListAsync(p, ct);
    }

    protected override IQueryable<FixedAsset> ApplyFilters(IQueryable<FixedAsset> q, PaginationParams p)
    {
        if (_costCenterFilter.HasValue) q = q.Where(a => a.CostCenterId == _costCenterFilter);
        if (_warehouseFilter.HasValue) q = q.Where(a => a.WarehouseId == _warehouseFilter);
        return q;
    }

    protected override IQueryable<FixedAsset> ApplySearch(IQueryable<FixedAsset> q, string t)
        => q.Where(a => a.AssetCode.Contains(t) || a.NameAr.Contains(t) || a.NameEn.Contains(t));

    protected override async Task ValidateAsync(CreateFixedAssetDto dto, FixedAsset? existing, CancellationToken ct)
    {
        if (existing?.DisposedAt != null) throw new ConflictException(string.Format(Messages.AssetAlreadyDisposed, existing.AssetCode));
        // بعد أول إهلاك مرحَّل: التكلفة ثابتة، ومجمع الإهلاك والقيمة الدفترية يديرهما ترحيل الإهلاك وحده.
        if (existing != null && await Db.Set<FixedAssetDepreciation>().AnyAsync(d => d.FixedAssetId == existing.Id, ct))
        {
            if (dto.PurchaseCost != existing.PurchaseCost) throw new ConflictException(Messages.CannotChangeCostAfterDepreciation);
            dto.CurrentBookValue = existing.CurrentBookValue;
            dto.AccumulatedDepreciation = existing.AccumulatedDepreciation;
        }

        // أصل له قيد اقتناء: تكلفته في الدفاتر، فلا تتغيّر من نموذج الأصل
        if (existing?.AcquisitionJournalEntryId != null && dto.PurchaseCost != existing.PurchaseCost)
            throw new ConflictException(Messages.CannotChangeCostAfterAcquisitionEntry);

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(dto.AssetCode)) errors.Add(Messages.AssetCodeRequired);
        if (string.IsNullOrWhiteSpace(dto.NameAr)) errors.Add(Messages.AssetArabicNameRequired);
        if (dto.PurchaseCost < 0) errors.Add(Messages.PurchaseCostCannotBeNegative);
        if (dto.DepreciationRate is < 0 or > 100) errors.Add(Messages.DepreciationRateRange);
        if (dto.SalvageValue < 0 || dto.SalvageValue > dto.PurchaseCost) errors.Add(Messages.SalvageValueRange);
        if (dto.CurrentBookValue < 0 || dto.CurrentBookValue > dto.PurchaseCost) errors.Add(Messages.BookValueRange);
        // مركز التكلفة إلزامي للأصل الجديد؛ الأصل القديم يبقى بلا مركز حتى يُحدَّد له، ولا يُفرَّغ بعد تحديده.
        if (dto.CostCenterId == null && (existing == null || existing.CostCenterId != null)) errors.Add(Messages.AssetCostCenterRequired);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        if (dto.CostCenterId != null && dto.CostCenterId != existing?.CostCenterId)
        {
            var active = await Db.Set<CostCenter>().Where(c => c.Id == dto.CostCenterId).Select(c => (bool?)c.IsActive).FirstOrDefaultAsync(ct)
                ?? throw new ValidationFailedException(string.Format(Messages.EntityNotFound, Messages.LabelCostCenter));
            if (!active) throw new ValidationFailedException(Messages.CostCenterInactive);
        }
        _previousCostCenterId = existing?.CostCenterId;

        // الموقع (المعرض) اختياري؛ عند تحديده أو تغييره يجب أن يكون موقعاً نشطاً للمنشأة.
        if (dto.WarehouseId != null && dto.WarehouseId != existing?.WarehouseId)
        {
            var status = await Db.Set<Warehouse>().Where(w => w.Id == dto.WarehouseId).Select(w => w.Status).FirstOrDefaultAsync(ct)
                ?? throw new ValidationFailedException(Messages.WarehouseNotFound);
            if (status != "active") throw new ValidationFailedException(Messages.WarehouseInactive);
        }

        // بلا حسابات محددة: حسابات الأصول الثابتة المادية ومجمع الإهلاك ومصروف الإهلاك الافتراضية في الشجرة.
        var noExpenseAccount = dto.DepreciationExpenseAccountId is null || dto.DepreciationExpenseAccountId == Guid.Empty;
        _dedicateAccumulated = existing == null && dto.AccumulatedDepreciationAccountId == Guid.Empty;
        _dedicateExpense = existing == null && noExpenseAccount;
        if (dto.AssetAccountId == Guid.Empty || dto.AccumulatedDepreciationAccountId == Guid.Empty || noExpenseAccount)
        {
            var defaults = new[] { DefaultAccounts.FixedAssets, DefaultAccounts.AccumulatedDepreciation, DefaultAccounts.DepreciationExpense };
            await DefaultAccounts.EnsureAsync(Db, ct, defaults);
            var ids = await Db.Set<Account>().Where(a => defaults.Contains(a.Code)).ToDictionaryAsync(a => a.Code, a => a.Id, ct);
            if (dto.AssetAccountId == Guid.Empty) dto.AssetAccountId = ids[DefaultAccounts.FixedAssets];
            if (dto.AccumulatedDepreciationAccountId == Guid.Empty) dto.AccumulatedDepreciationAccountId = ids[DefaultAccounts.AccumulatedDepreciation];
            if (noExpenseAccount) dto.DepreciationExpenseAccountId = ids[DefaultAccounts.DepreciationExpense];
        }

        if (!await Db.Set<Account>().AnyAsync(a => a.Id == dto.AssetAccountId, ct))
            throw new ValidationFailedException(Messages.AssetAccountNotFound);
        if (!await Db.Set<Account>().AnyAsync(a => a.Id == dto.AccumulatedDepreciationAccountId, ct))
            throw new ValidationFailedException(Messages.AccumulatedDepreciationAccountNotFound);
        if (!await Db.Set<Account>().AnyAsync(a => a.Id == dto.DepreciationExpenseAccountId, ct))
            throw new ValidationFailedException(Messages.DepreciationExpenseAccountNotFound);
        if (await Db.Set<FixedAsset>().AnyAsync(a => a.AssetCode == dto.AssetCode && (existing == null || a.Id != existing.Id), ct))
            throw new ConflictException(Messages.AssetCodeInUse);
    }

    /// <summary>
    /// قيد الاقتناء عند التسجيل إن حُدِّد حساب التمويل: مدين حساب الأصل بتكلفته، دائن مجمع الإهلاك بما أُهلك سابقاً
    /// (أصل قائم يُدخَل بقيمته الدفترية)، ودائن حساب التمويل بالقيمة الدفترية.
    /// </summary>
    protected override async Task OnCreatingAsync(FixedAsset e, CreateFixedAssetDto dto, CancellationToken ct)
    {
        await CreateDedicatedAccountsAsync(e, ct);
        if (string.IsNullOrWhiteSpace(dto.AcquisitionAccountCode)) return;
        var ids = new[] { e.AssetAccountId, e.AccumulatedDepreciationAccountId };
        var codes = await Db.Set<Account>().AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Code, ct);
        var accumulated = e.PurchaseCost - e.CurrentBookValue;
        var note = $"اقتناء الأصل {e.AssetCode}";
        var lines = new List<PostingLine> { new(codes[e.AssetAccountId], e.PurchaseCost, 0, note) };
        if (accumulated > 0) lines.Add(new(codes[e.AccumulatedDepreciationAccountId], 0, accumulated, note));
        if (e.CurrentBookValue > 0) lines.Add(new(dto.AcquisitionAccountCode.Trim(), 0, e.CurrentBookValue, note));
        if (e.PurchaseCost <= 0) return;

        var posted = await _posting.PostAsync(new GenericPostingRequest
        {
            Date = e.PurchaseDate > SaudiTime.Today ? SaudiTime.Today : e.PurchaseDate, Description = $"{note} - {e.NameAr}",
            SourceType = "fixed_asset_acquisition", SourceId = e.Id, SourceNumber = e.AssetCode, Lines = lines,
        }, ct);
        e.AcquisitionJournalEntryId = posted.JournalEntryId;
    }

    /// <summary>
    /// حسابا الأصل المخصصان: مجمع إهلاكه تحت مجمع الإهلاك (122) ومصروف إهلاكه تحت مصروف الإهلاك (523)، مرتبطان بمعرّف الأصل،
    /// ويُحفظ معرّفاهما على الأصل. حساب حدّده الطلب صراحةً يُستخدم كما هو. داخل معاملة الأصل فلا يبقى حساب بلا أصل.
    /// </summary>
    /// <remarks>
    /// المحرك يرفض الترحيل على حساب له فروع: إن كان الحساب الافتراضي مستخدماً مباشرة (أصول قديمة عليه أو قيود أو إعداد)
    /// يبقى الأصل الجديد عليه كما كان، فلا يتعطل إهلاك الأصول القائمة ولا قيودها.
    /// </remarks>
    private async Task CreateDedicatedAccountsAsync(FixedAsset e, CancellationToken ct)
    {
        if (_dedicateAccumulated && await _accounts.CanHoldSubAccountsAsync(DefaultAccounts.AccumulatedDepreciation, ct))
        {
            var (nameAr, nameEn) = AccumulatedNames(e);
            e.AccumulatedDepreciationAccountId = (await _accounts.CreateLinkedChildAccountAsync(
                DefaultAccounts.AccumulatedDepreciation, LinkedEntityType.FixedAsset, e.Id, nameAr, nameEn, ct)).Id;
        }
        if (_dedicateExpense && await _accounts.CanHoldSubAccountsAsync(DefaultAccounts.DepreciationExpense, ct))
        {
            var (nameAr, nameEn) = ExpenseNames(e);
            e.DepreciationExpenseAccountId = (await _accounts.CreateLinkedChildAccountAsync(
                DefaultAccounts.DepreciationExpense, LinkedEntityType.FixedAsset, e.Id, nameAr, nameEn, ct)).Id;
        }
    }

    private static (string Ar, string En) AccumulatedNames(FixedAsset e)
        => ($"مجمع إهلاك {e.NameAr} ({e.AssetCode})", $"Accumulated Depreciation - {EnglishName(e)} ({e.AssetCode})");

    private static (string Ar, string En) ExpenseNames(FixedAsset e)
        => ($"مصروف إهلاك {e.NameAr} ({e.AssetCode})", $"Depreciation Expense - {EnglishName(e)} ({e.AssetCode})");

    private static string EnglishName(FixedAsset e) => string.IsNullOrWhiteSpace(e.NameEn) ? e.NameAr : e.NameEn;

    /// <summary>
    /// الحسابات المخصصة للأصل ثابتة عليه (لا يستبدلها التعديل)، وتغيير اسمه أو رمزه يعيد تسمية حساباته فقط دون تغيير أرقامها.
    /// </summary>
    protected override async Task OnUpdatingAsync(FixedAsset e, UpdateFixedAssetDto dto, CancellationToken ct)
    {
        // الدور من نوع الحساب: المصروف = مصروف الإهلاك، وغيره = مجمع الإهلاك (يعيد الحساب المخصص لأصل فُرِّغ حقله أيضاً)
        var dedicated = await DedicatedAccountsQuery(e.Id).ToListAsync(ct);
        if (dedicated.FirstOrDefault(a => a.Type != AccountCategory.Expense) is { } accumulated)
        {
            e.AccumulatedDepreciationAccountId = accumulated.Id;
            (accumulated.NameAr, accumulated.NameEn) = AccumulatedNames(e);
        }
        if (dedicated.FirstOrDefault(a => a.Type == AccountCategory.Expense) is { } expense)
        {
            e.DepreciationExpenseAccountId = expense.Id;
            (expense.NameAr, expense.NameEn) = ExpenseNames(e);
        }
    }

    /// <summary>
    /// حذف الأصل يحذف حساباته المخصصة معه، ويُمنع إن كان للأصل أثر في الدفاتر (قيد اقتناء أو إهلاك مرحَّل أو استبعاد أو أي قيد
    /// على حساباته المخصصة): يُستبعد الأصل حينها بدلاً من حذفه فيبقى أثره التاريخي.
    /// </summary>
    protected override async Task OnDeletingAsync(FixedAsset e, CancellationToken ct)
    {
        var dedicated = await DedicatedAccountsQuery(e.Id).ToListAsync(ct);
        var codes = dedicated.Select(a => a.Code).ToList();
        if (e.DisposedAt != null || e.AcquisitionJournalEntryId != null
            || await Db.Set<FixedAssetDepreciation>().AnyAsync(d => d.FixedAssetId == e.Id, ct)
            || (codes.Count > 0 && await Db.Set<JournalEntryLine>().AnyAsync(l => codes.Contains(l.AccountCode), ct)))
            throw new ConflictException(Messages.CannotDeleteFixedAssetWithEntries);
        if (codes.Count > 0 && await Db.Set<Account>().AnyAsync(a => a.ParentCode != null && codes.Contains(a.ParentCode), ct))
            throw new ConflictException(Messages.CannotDeleteAccountWithChildren);
        _accountsToDelete = dedicated;
    }

    /// <summary>بعد حذف الأصل (المفتاح الأجنبي إليها مقيَّد) تُحذف حساباته المخصصة في المعاملة نفسها.</summary>
    protected override async Task OnDeletedAsync(FixedAsset e, CancellationToken ct)
    {
        if (_accountsToDelete.Count == 0) return;
        Db.RemoveRange(_accountsToDelete);
        await SaveAsync(ct);
        _accountsToDelete = new();
    }

    private IQueryable<Account> DedicatedAccountsQuery(Guid assetId)
        => Db.Set<Account>().Where(a => a.LinkedEntityType == LinkedEntityType.FixedAsset && a.LinkedEntityId == assetId);

    protected override async Task OnUpdatedAsync(FixedAsset entity, CancellationToken ct)
    {
        if (entity.CostCenterId == _previousCostCenterId) return;
        var ids = new[] { _previousCostCenterId, entity.CostCenterId }.Where(id => id.HasValue).Select(id => id!.Value).ToList();
        var codes = await Db.Set<CostCenter>().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, ct);
        string Code(Guid? id) => id.HasValue && codes.TryGetValue(id.Value, out var code) ? code : "—";
        await _audit.LogAsync("COST_CENTER_CHANGED", nameof(FixedAsset), entity.Id.ToString(),
            $"تغيير مركز تكلفة الأصل {entity.AssetCode}: {Code(_previousCostCenterId)} ← {Code(entity.CostCenterId)}", ct);
    }
}
