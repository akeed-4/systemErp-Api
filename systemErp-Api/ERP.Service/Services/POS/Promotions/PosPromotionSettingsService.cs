using ERP.Core.Contracts.POS;
using ERP.Core.DTOs.POS;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.POS;

public class PosPromotionSettingsService : IPosPromotionSettingsService
{
    private readonly ErpDbContext _db;
    public PosPromotionSettingsService(ErpDbContext db) => _db = db;

    public async Task<PosPromotionSettingsDto> GetAsync(CancellationToken ct = default)
        => Mapper.Map<PosPromotionSettingsDto>(await PosPricing.SettingsAsync(_db, ct));

    public async Task<PosPromotionSettingsDto> UpdateAsync(UpdatePosPromotionSettingsDto r, CancellationToken ct = default)
    {
        if (r.MaxLineDiscountPercent is < 0 or > 100 || r.MaxInvoiceDiscountPercent is < 0 or > 100 || r.MaxRedeemPercent is < 0 or > 100)
            throw new ValidationFailedException(Messages.PercentMustBe0To100);
        if (r.SarPerEarnedPoint <= 0 || r.PointValueSar <= 0 || r.MinPointsToRedeem < 0 || r.PointsPendingDays is < 0 or > 3650)
            throw new ValidationFailedException(Messages.LoyaltyRatesInvalid);
        if (!(r.SilverThreshold > 0 && r.SilverThreshold < r.GoldThreshold && r.GoldThreshold < r.PlatinumThreshold))
            throw new ValidationFailedException(Messages.LoyaltyTierThresholdsOrder);

        var s = await _db.Set<PosPromotionSettings>().FirstOrDefaultAsync(ct);
        if (s == null) { s = new PosPromotionSettings(); _db.Add(s); }
        var revalue = s.PointValueSar != r.PointValueSar;
        var retier = s.SilverThreshold != r.SilverThreshold || s.GoldThreshold != r.GoldThreshold || s.PlatinumThreshold != r.PlatinumThreshold;
        Mapper.Apply(r, s);
        await _db.SaveChangesAsync(ct);

        // الأرصدة الحالية بالقيمة والفئات الجديدة
        if (revalue)
            await _db.Set<CustomerLoyalty>().ExecuteUpdateAsync(u => u.SetProperty(l => l.PointsValueSar, l => l.PointsBalance * r.PointValueSar), ct);
        if (retier)
            await _db.Set<CustomerLoyalty>().ExecuteUpdateAsync(u => u.SetProperty(l => l.Tier, l =>
                l.TotalPointsEarned >= r.PlatinumThreshold ? LoyaltyTier.Platinum
                : l.TotalPointsEarned >= r.GoldThreshold ? LoyaltyTier.Gold
                : l.TotalPointsEarned >= r.SilverThreshold ? LoyaltyTier.Silver : LoyaltyTier.Bronze), ct);
        return Mapper.Map<PosPromotionSettingsDto>(s);
    }
}
