using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface ICurrencyService : ICrudService<CurrencyDto, CreateCurrencyDto, UpdateCurrencyDto> { }
