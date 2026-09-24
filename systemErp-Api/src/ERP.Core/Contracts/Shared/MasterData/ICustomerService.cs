using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface ICustomerService : ICrudService<CustomerDto, CreateCustomerDto, UpdateCustomerDto> { }
