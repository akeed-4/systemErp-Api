using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

public class FixedAssetService : CrudService<FixedAsset, FixedAssetDto, CreateFixedAssetDto, UpdateFixedAssetDto>, IFixedAssetService
{
    public FixedAssetService(ErpDbContext db) : base(db) { }
    protected override string Label => Messages.LabelFixedAsset;

    protected override IQueryable<FixedAsset> ApplySearch(IQueryable<FixedAsset> q, string t)
        => q.Where(a => a.AssetCode.Contains(t) || a.NameAr.Contains(t) || a.NameEn.Contains(t));

    protected override async Task ValidateAsync(CreateFixedAssetDto dto, FixedAsset? existing, CancellationToken ct)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(dto.AssetCode)) errors.Add(Messages.AssetCodeRequired);
        if (string.IsNullOrWhiteSpace(dto.NameAr)) errors.Add(Messages.AssetArabicNameRequired);
        if (dto.PurchaseCost < 0) errors.Add(Messages.PurchaseCostCannotBeNegative);
        if (dto.DepreciationRate is < 0 or > 100) errors.Add(Messages.DepreciationRateRange);
        if (dto.CurrentBookValue < 0 || dto.CurrentBookValue > dto.PurchaseCost) errors.Add(Messages.BookValueRange);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        // بلا حسابات محددة: حساب الأصول الثابتة المادية ومجمع الإهلاك الافتراضيان في الشجرة.
        if (dto.AssetAccountId == Guid.Empty || dto.AccumulatedDepreciationAccountId == Guid.Empty)
        {
            await DefaultAccounts.EnsureAsync(Db, ct, DefaultAccounts.FixedAssets, DefaultAccounts.AccumulatedDepreciation);
            var ids = await Db.Set<Account>().Where(a => a.Code == DefaultAccounts.FixedAssets || a.Code == DefaultAccounts.AccumulatedDepreciation)
                .ToDictionaryAsync(a => a.Code, a => a.Id, ct);
            if (dto.AssetAccountId == Guid.Empty) dto.AssetAccountId = ids[DefaultAccounts.FixedAssets];
            if (dto.AccumulatedDepreciationAccountId == Guid.Empty) dto.AccumulatedDepreciationAccountId = ids[DefaultAccounts.AccumulatedDepreciation];
        }

        if (!await Db.Set<Account>().AnyAsync(a => a.Id == dto.AssetAccountId, ct))
            throw new ValidationFailedException(Messages.AssetAccountNotFound);
        if (!await Db.Set<Account>().AnyAsync(a => a.Id == dto.AccumulatedDepreciationAccountId, ct))
            throw new ValidationFailedException(Messages.AccumulatedDepreciationAccountNotFound);
        if (await Db.Set<FixedAsset>().AnyAsync(a => a.AssetCode == dto.AssetCode && (existing == null || a.Id != existing.Id), ct))
            throw new ConflictException(Messages.AssetCodeInUse);
    }
}
