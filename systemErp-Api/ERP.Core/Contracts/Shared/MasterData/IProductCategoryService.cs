using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface IProductCategoryService : ICrudService<ProductCategoryDto, CreateProductCategoryDto, UpdateProductCategoryDto> { }
