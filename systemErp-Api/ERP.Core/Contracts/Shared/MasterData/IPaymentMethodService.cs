using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface IPaymentMethodService : ICrudService<PaymentMethodItemDto, CreatePaymentMethodItemDto, UpdatePaymentMethodItemDto>
{
    /// <summary>الطرق الفعّالة لقوائم الدفع في المستندات (مرتبة بالرمز).</summary>
    Task<List<PaymentMethodItemDto>> ListActiveAsync(CancellationToken ct = default);
}
