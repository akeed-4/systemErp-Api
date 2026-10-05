using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

public class FixedAssetService : CrudService<FixedAsset, FixedAssetDto, CreateFixedAssetDto, UpdateFixedAssetDto>, IFixedAssetService
{
    private readonly IAuditService _audit;
    private Guid? _costCenterFilter;
    private Guid? _previousCostCenterId;

    public FixedAssetService(ErpDbContext db, IAuditService audit) : base(db) => _audit = audit;
    protected override string Label => Messages.LabelFixedAsset;

    public Task<PagedResult<FixedAssetDto>> ListAsync(Guid? costCenterId, PaginationParams p, CancellationToken ct = default)
    {
        _costCenterFilter = costCenterId;
        return ListAsync(p, ct);
    }

    protected override IQueryable<FixedAsset> ApplyFilters(IQueryable<FixedAsset> q, PaginationParams p)
        => _costCenterFilter.HasValue ? q.Where(a => a.CostCenterId == _costCenterFilter) : q;

    protected override IQueryable<FixedAsset> ApplySearch(IQueryable<FixedAsset> q, string t)
        => q.Where(a => a.AssetCode.Contains(t) || a.NameAr.Contains(t) || a.NameEn.Contains(t));

    protected override async Task ValidateAsync(CreateFixedAssetDto dto, FixedAsset? existing, CancellationToken ct)
    {
        // بعد أول إهلاك مرحَّل: التكلفة ثابتة، ومجمع الإهلاك والقيمة الدفترية يديرهما ترحيل الإهلاك وحده.
        if (existing != null && await Db.Set<FixedAssetDepreciation>().AnyAsync(d => d.FixedAssetId == existing.Id, ct))
        {
            if (dto.PurchaseCost != existing.PurchaseCost) throw new ConflictException(Messages.CannotChangeCostAfterDepreciation);
            dto.CurrentBookValue = existing.CurrentBookValue;
            dto.AccumulatedDepreciation = existing.AccumulatedDepreciation;
        }

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(dto.AssetCode)) errors.Add(Messages.AssetCodeRequired);
        if (string.IsNullOrWhiteSpace(dto.NameAr)) errors.Add(Messages.AssetArabicNameRequired);
        if (dto.PurchaseCost < 0) errors.Add(Messages.PurchaseCostCannotBeNegative);
        if (dto.DepreciationRate is < 0 or > 100) errors.Add(Messages.DepreciationRateRange);
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

        // بلا حسابات محددة: حسابات الأصول الثابتة المادية ومجمع الإهلاك ومصروف الإهلاك الافتراضية في الشجرة.
        var noExpenseAccount = dto.DepreciationExpenseAccountId is null || dto.DepreciationExpenseAccountId == Guid.Empty;
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
