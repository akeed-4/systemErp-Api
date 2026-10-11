using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface IProductService : ICrudService<ProductDto, CreateProductDto, UpdateProductDto>
{
    /// <summary>استيراد جماعي للأصناف (من Excel). صف فاشل لا يوقف البقية.</summary>
    Task<ImportResultDto> ImportAsync(List<CreateProductDto> items, CancellationToken ct = default);
}
