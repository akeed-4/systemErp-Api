using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface IBankService : ICrudService<BankEntityDto, CreateBankEntityDto, UpdateBankEntityDto> { }
