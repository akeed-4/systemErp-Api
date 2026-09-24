using ERP.Core.Contracts.POS;
using ERP.Core.DTOs.POS;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.POS;

public class PosOfferService : CrudService<PosOffer, PosOfferDto, CreatePosOfferDto, UpdatePosOfferDto>, IPosOfferService
{
    public PosOfferService(ErpDbContext db) : base(db) { }
    protected override string Label => "العرض";

    protected override async Task ValidateAsync(CreatePosOfferDto d, PosOffer? existing, CancellationToken ct)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(d.TitleAr)) errors.Add("عنوان العرض مطلوب.");
        switch (d.Type)
        {
            case PosOfferType.PercentageDiscount or PosOfferType.CategoryDiscount:
                if (d.DiscountPercent is null or <= 0 or > 100) errors.Add("نسبة الخصم بين 0 و100.");
                break;
            case PosOfferType.FixedDiscount:
                if (d.DiscountAmount is null or <= 0) errors.Add("مبلغ الخصم يجب أن يكون موجباً.");
                break;
            case PosOfferType.BuyXGetY:
                if (d.BuyQuantity is null or <= 0 || d.GetQuantity is null or <= 0) errors.Add("كميتا الشراء والهدية يجب أن تكونا موجبتين.");
                break;
        }
        if (d.Type == PosOfferType.CategoryDiscount && d.TargetCategoryId == null) errors.Add("عرض التصنيف يتطلب تحديد التصنيف.");
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
        if (d.TargetCategoryId.HasValue && !await Db.Set<ProductCategory>().AnyAsync(c => c.Id == d.TargetCategoryId, ct))
            throw new ValidationFailedException("التصنيف غير موجود.");
        if (d.TargetItemId.HasValue && !await Db.Set<Product>().AnyAsync(p => p.Id == d.TargetItemId, ct))
            throw new ValidationFailedException("الصنف غير موجود.");
    }
}
