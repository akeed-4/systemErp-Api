using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface IPaymentMethodService : ICrudService<PaymentMethodItemDto, CreatePaymentMethodItemDto, UpdatePaymentMethodItemDto> { }
