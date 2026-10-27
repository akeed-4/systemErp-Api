using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// رموز خصم الاشتراكات (على مستوى المنصة في القاعدة المشتركة): الخصم من سعر الباقة قبل الضريبة، بشروط
/// الباقات ودورة الفوترة والصلاحية وحدّي الاستخدام (المنصة كلها والمنشأة الواحدة). الاستخدام يُسجَّل عند التفعيل فقط.
/// </summary>
public class PromoCodeService : IPromoCodeService
{
    private readonly ErpDbContext _db;
    private readonly ITenantContext _tenant;

    public PromoCodeService(ErpDbContext db, ITenantContext tenant)
    {
        _db = db; _tenant = tenant;
    }

    private ErpDbContext Catalog => _db.Catalog;

    // ---------- إدارة المنصة ----------

    public async Task<List<PromoCodeDto>> ListAsync(CancellationToken ct = default)
    {
        var usage = await Catalog.Set<PromoCodeRedemption>().AsNoTracking()
            .GroupBy(r => r.PromoCodeId).Select(g => new { g.Key, Count = g.Count(), Total = g.Sum(r => r.DiscountAmount) })
            .ToDictionaryAsync(x => x.Key, ct);
        return (await Catalog.Set<PromoCode>().AsNoTracking().OrderByDescending(p => p.CreatedAt).ToListAsync(ct))
            .Select(p => ToDto(p, usage.GetValueOrDefault(p.Id)?.Count ?? 0, usage.GetValueOrDefault(p.Id)?.Total ?? 0)).ToList();
    }

    public async Task<PromoCodeDto> CreateAsync(SavePromoCodeRequestDto r, CancellationToken ct = default)
    {
        var promo = new PromoCode();
        await ApplyAsync(promo, r, ct);
        Catalog.Add(promo);
        await Catalog.SaveChangesAsync(ct);
        return ToDto(promo, 0, 0);
    }

    public async Task<PromoCodeDto> UpdateAsync(Guid id, SavePromoCodeRequestDto r, CancellationToken ct = default)
    {
        var promo = await Catalog.Set<PromoCode>().FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException(Messages.PromoCodeNotFound);
        await ApplyAsync(promo, r, ct);
        promo.UpdatedAt = DateTime.UtcNow;
        await Catalog.SaveChangesAsync(ct);
        var used = Catalog.Set<PromoCodeRedemption>().Where(x => x.PromoCodeId == id);
        return ToDto(promo, await used.CountAsync(ct), await used.SumAsync(x => (decimal?)x.DiscountAmount, ct) ?? 0);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var promo = await Catalog.Set<PromoCode>().FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException(Messages.PromoCodeNotFound);
        if (await Catalog.Set<PromoCodeRedemption>().AnyAsync(x => x.PromoCodeId == id, ct))
            throw new ConflictException(Messages.PromoCodeUsedCannotDelete);
        Catalog.Remove(promo);
        await Catalog.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(PromoCode p, SavePromoCodeRequestDto r, CancellationToken ct)
    {
        var code = Normalize(r.Code);
        if (code.Length is < 3 or > 32 || !code.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new ValidationFailedException(Messages.PromoCodeFormat);
        if (await Catalog.Set<PromoCode>().AnyAsync(x => x.Code == code && x.Id != p.Id, ct))
            throw new ConflictException(string.Format(Messages.PromoCodeDuplicate, code));
        var type = (r.DiscountType ?? string.Empty).Trim().ToLowerInvariant();
        if (type is not (PromoDiscountTypes.Percent or PromoDiscountTypes.Fixed)) throw new ValidationFailedException(Messages.PromoCodeTypeInvalid);
        if (r.Value <= 0 || (type == PromoDiscountTypes.Percent && r.Value > 100)) throw new ValidationFailedException(Messages.PromoCodeValueInvalid);
        if (r.ValidFrom.HasValue && r.ValidUntil.HasValue && r.ValidUntil.Value.Date < r.ValidFrom.Value.Date)
            throw new ValidationFailedException(Messages.EndDateBeforeStart);
        if (r.MaxRedemptions is < 1 || r.MaxPerTenant < 1) throw new ValidationFailedException(Messages.PromoCodeLimitInvalid);
        if (r.BillingCycle.HasValue && !Enum.IsDefined(r.BillingCycle.Value)) throw new ValidationFailedException(Messages.InvalidPlanOrBillingCycle);

        var plans = (r.PlanIds ?? new()).Select(SubscriptionPlanId.Normalize).Where(x => x.Length > 0).Distinct().ToList();
        if (plans.Count > 0 && await Catalog.Set<PlanDefinition>().CountAsync(x => plans.Contains(x.Id), ct) != plans.Count)
            throw new ValidationFailedException(Messages.PromoCodePlanUnknown);

        p.Code = code;
        p.Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
        p.DiscountType = type;
        p.Value = Math.Round(r.Value, 2);
        p.PlanIds = plans.Count == 0 ? null : string.Join(',', plans);
        p.BillingCycle = r.BillingCycle;
        p.ValidFrom = r.ValidFrom?.Date;
        p.ValidUntil = r.ValidUntil?.Date;
        p.MaxRedemptions = r.MaxRedemptions;
        p.MaxPerTenant = r.MaxPerTenant;
        p.IsActive = r.IsActive;
    }

    // ---------- التسعير ----------

    public async Task<SubscriptionQuoteDto> QuoteAsync(SubscriptionQuoteRequestDto r, CancellationToken ct = default)
    {
        if (!SubscriptionPlanId.IsValid(r.PlanId) || !Enum.IsDefined(r.BillingCycle)) throw new ValidationFailedException(Messages.InvalidPlanOrBillingCycle);
        var plan = await SubscriptionCatalog.GetAsync(_db, r.PlanId, forSale: true, ct);
        var tenantId = _tenant.TenantId ?? throw new UnauthorizedAppException();
        return (await PriceAsync(_db, tenantId, plan, r.BillingCycle, r.PromoCode, ct)).Quote;
    }

    /// <summary>
    /// سعر الباقة بعد الرمز: الخصم من السعر قبل الضريبة ثم ضريبة الصافي. الرمز غير الصالح يُرفض برسالة تذكر السبب
    /// (غير موجود/معطّل، خارج الصلاحية، لا ينطبق على الباقة أو الدورة، استُنفد، أو استخدمته المنشأة).
    /// </summary>
    public static async Task<(SubscriptionQuoteDto Quote, PromoCode? Promo)> PriceAsync(ErpDbContext db, Guid tenantId, SubscriptionPlanDto plan,
        SubscriptionBillingCycle cycle, string? rawCode, CancellationToken ct)
    {
        var price = cycle == SubscriptionBillingCycle.Yearly ? plan.PriceYearly : plan.PriceMonthly;
        PromoCode? promo = null;
        decimal discount = 0;
        var code = Normalize(rawCode);
        if (code.Length > 0)
        {
            promo = await db.Catalog.Set<PromoCode>().AsNoTracking().FirstOrDefaultAsync(p => p.Code == code, ct);
            if (promo == null || !promo.IsActive) throw new ValidationFailedException(Messages.PromoCodeInvalid);
            var today = SaudiTime.Today;
            if (promo.ValidFrom.HasValue && today < promo.ValidFrom.Value.Date) throw new ValidationFailedException(Messages.PromoCodeNotStarted);
            if (promo.ValidUntil.HasValue && today > promo.ValidUntil.Value.Date) throw new ValidationFailedException(Messages.PromoCodeExpired);
            var plans = (promo.PlanIds ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (plans.Length > 0 && !plans.Contains(plan.Id)) throw new ValidationFailedException(Messages.PromoCodeNotForPlan);
            if (promo.BillingCycle.HasValue && promo.BillingCycle != cycle) throw new ValidationFailedException(Messages.PromoCodeNotForCycle);
            var id = promo.Id;
            if (promo.MaxRedemptions.HasValue && await db.Catalog.Set<PromoCodeRedemption>().CountAsync(x => x.PromoCodeId == id, ct) >= promo.MaxRedemptions)
                throw new ValidationFailedException(Messages.PromoCodeExhausted);
            if (await db.Catalog.Set<PromoCodeRedemption>().CountAsync(x => x.PromoCodeId == id && x.TenantId == tenantId, ct) >= promo.MaxPerTenant)
                throw new ValidationFailedException(Messages.PromoCodeAlreadyUsed);
            discount = promo.DiscountType == PromoDiscountTypes.Percent ? Math.Round(price * promo.Value / 100m, 2) : Math.Min(promo.Value, price);
        }
        var net = price - discount;
        var vat = Math.Round(net * SubscriptionCatalog.VatRate, 2);
        return (new SubscriptionQuoteDto
        {
            PlanId = plan.Id, BillingCycle = cycle, Price = price, Discount = discount, Net = net, Vat = vat, Total = net + vat,
            PromoCode = promo?.Code, PromoDescription = promo?.Description,
        }, promo);
    }

    /// <summary>يسجّل استخدام الرمز عند تفعيل الاشتراك (على القاعدة المشتركة).</summary>
    public static async Task RecordAsync(ErpDbContext db, string code, Guid tenantId, Guid? paymentId, Subscription sub, CancellationToken ct)
    {
        var promo = await db.Catalog.Set<PromoCode>().AsNoTracking().FirstOrDefaultAsync(p => p.Code == code, ct);
        if (promo == null) return;
        db.Catalog.Add(new PromoCodeRedemption
        {
            PromoCodeId = promo.Id, Code = promo.Code, TenantId = tenantId, PaymentId = paymentId, SubscriptionId = sub.Id,
            PlanId = sub.PlanType, DiscountAmount = sub.DiscountAmount,
        });
        if (!ReferenceEquals(db.Catalog, db)) await db.Catalog.SaveChangesAsync(ct);
    }

    public static string Normalize(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    private static PromoCodeDto ToDto(PromoCode p, int redemptions, decimal totalDiscount) => new()
    {
        Id = p.Id, Code = p.Code, Description = p.Description, DiscountType = p.DiscountType, Value = p.Value,
        PlanIds = (p.PlanIds ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
        BillingCycle = p.BillingCycle, ValidFrom = p.ValidFrom, ValidUntil = p.ValidUntil, MaxRedemptions = p.MaxRedemptions,
        MaxPerTenant = p.MaxPerTenant, IsActive = p.IsActive, Redemptions = redemptions, TotalDiscount = totalDiscount, CreatedAt = p.CreatedAt,
    };
}
