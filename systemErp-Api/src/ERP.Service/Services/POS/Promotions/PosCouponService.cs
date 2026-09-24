using ERP.Core.Contracts.POS;
using ERP.Core.DTOs.POS;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.POS;

public class PosCouponService : CrudService<PosCoupon, PosCouponDto, CreatePosCouponDto, UpdatePosCouponDto>, IPosCouponService
{
    public PosCouponService(ErpDbContext db) : base(db) { }
    protected override string Label => "الكوبون";

    protected override IQueryable<PosCoupon> ApplySearch(IQueryable<PosCoupon> q, string t)
        => q.Where(c => c.Code.Contains(t) || c.TitleAr.Contains(t));

    protected override async Task ValidateAsync(CreatePosCouponDto d, PosCoupon? existing, CancellationToken ct)
    {
        var errors = new List<string>();
        d.Code = (d.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (d.Code.Length == 0) errors.Add("كود الكوبون مطلوب.");
        if (string.IsNullOrWhiteSpace(d.TitleAr)) errors.Add("عنوان الكوبون مطلوب.");
        if (d.DiscountValue <= 0) errors.Add("قيمة الخصم يجب أن تكون موجبة.");
        if (d.DiscountType == PosDiscountType.Percent && d.DiscountValue > 100) errors.Add("نسبة الخصم لا تتجاوز 100%.");
        if (d.MinCartAmount < 0 || d.MaxDiscountAmount < 0) errors.Add("الحدود لا تكون سالبة.");
        if (d.ValidTo < d.ValidFrom) errors.Add("نهاية الصلاحية قبل بدايتها.");
        if (d.UsageLimit is <= 0) errors.Add("حد الاستخدام يجب أن يكون موجباً.");
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
        if (await Db.Set<PosCoupon>().AnyAsync(c => c.Code == d.Code && (existing == null || c.Id != existing.Id), ct))
            throw new ConflictException("كود الكوبون مستخدم مسبقاً.");
    }

    protected override Task OnCreatingAsync(PosCoupon e, CreatePosCouponDto d, CancellationToken ct) { e.UsageCount = 0; return Task.CompletedTask; }

    protected override Task OnUpdatingAsync(PosCoupon e, UpdatePosCouponDto d, CancellationToken ct)
    {
        e.UsageCount = Db.Entry(e).OriginalValues.GetValue<int>(nameof(PosCoupon.UsageCount)); // العدّاد يزيد بالبيع فقط
        return Task.CompletedTask;
    }

    public async Task<CouponValidationResultDto> ValidateAsync(CouponValidationRequestDto r, CancellationToken ct = default)
    {
        var code = (r.Code ?? string.Empty).Trim().ToUpperInvariant();
        var coupon = await Db.Set<PosCoupon>().AsNoTracking().FirstOrDefaultAsync(c => c.Code == code, ct);
        var reason = PosPricing.CouponRejectionReason(coupon, r.CartAmount, DateTime.UtcNow);
        if (reason != null) return new CouponValidationResultDto { IsValid = false, Message = reason };
        return new CouponValidationResultDto
        {
            IsValid = true, Message = "تم تطبيق الكوبون", Coupon = Mapper.Map<PosCouponDto>(coupon!),
            DiscountAmount = PosPricing.CouponDiscount(coupon!, r.CartAmount),
        };
    }
}
