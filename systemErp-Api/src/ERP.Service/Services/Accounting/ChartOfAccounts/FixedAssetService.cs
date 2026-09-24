using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

public class FixedAssetService : CrudService<FixedAsset, FixedAssetDto, CreateFixedAssetDto, UpdateFixedAssetDto>, IFixedAssetService
{
    public FixedAssetService(ErpDbContext db) : base(db) { }
    protected override string Label => "الأصل الثابت";

    protected override IQueryable<FixedAsset> ApplySearch(IQueryable<FixedAsset> q, string t)
        => q.Where(a => a.AssetCode.Contains(t) || a.NameAr.Contains(t) || a.NameEn.Contains(t));

    protected override async Task ValidateAsync(CreateFixedAssetDto dto, FixedAsset? existing, CancellationToken ct)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(dto.AssetCode)) errors.Add("كود الأصل مطلوب.");
        if (string.IsNullOrWhiteSpace(dto.NameAr)) errors.Add("اسم الأصل بالعربية مطلوب.");
        if (dto.PurchaseCost < 0) errors.Add("تكلفة الشراء لا تكون سالبة.");
        if (dto.DepreciationRate is < 0 or > 100) errors.Add("نسبة الإهلاك بين 0 و100.");
        if (dto.CurrentBookValue < 0 || dto.CurrentBookValue > dto.PurchaseCost) errors.Add("القيمة الدفترية بين صفر وتكلفة الشراء.");
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        if (!await Db.Set<Account>().AnyAsync(a => a.Id == dto.AssetAccountId, ct))
            throw new ValidationFailedException("حساب الأصل غير موجود.");
        if (!await Db.Set<Account>().AnyAsync(a => a.Id == dto.AccumulatedDepreciationAccountId, ct))
            throw new ValidationFailedException("حساب مجمع الإهلاك غير موجود.");
        if (await Db.Set<FixedAsset>().AnyAsync(a => a.AssetCode == dto.AssetCode && (existing == null || a.Id != existing.Id), ct))
            throw new ConflictException("كود الأصل مستخدم مسبقاً.");
    }
}
