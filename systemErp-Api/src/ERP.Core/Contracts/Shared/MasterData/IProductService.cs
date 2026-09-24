using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface IProductService : ICrudService<ProductDto, CreateProductDto, UpdateProductDto> { }
