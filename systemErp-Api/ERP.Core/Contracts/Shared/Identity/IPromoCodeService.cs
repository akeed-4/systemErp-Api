using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>رموز خصم الاشتراكات: إدارة مسؤول المنصة، وتسعير المنشأة قبل الدفع.</summary>
public interface IPromoCodeService
{
    Task<List<PromoCodeDto>> ListAsync(CancellationToken ct = default);
    Task<PromoCodeDto> CreateAsync(SavePromoCodeRequestDto request, CancellationToken ct = default);
    Task<PromoCodeDto> UpdateAsync(Guid id, SavePromoCodeRequestDto request, CancellationToken ct = default);
    /// <summary>حذف رمز لم يُستخدم؛ المستخدم يُعطَّل بدل حذفه.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>تسعير باقة للمنشأة الحالية مع رمز خصم اختياري (يرفض الرمز غير الصالح برسالة واضحة).</summary>
    Task<SubscriptionQuoteDto> QuoteAsync(SubscriptionQuoteRequestDto request, CancellationToken ct = default);
}
