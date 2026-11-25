using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface IProductService : ICrudService<ProductDto, CreateProductDto, UpdateProductDto>
{
    /// <summary>استيراد جماعي للأصناف (من Excel). صف فاشل لا يوقف البقية.</summary>
    Task<ImportResultDto> ImportAsync(List<CreateProductDto> items, CancellationToken ct = default);

    /// <summary>دخل الصنف حركة أو مستنداً: تكلفته المعيارية لا يغيّرها إلا صاحب صلاحية إعادة التقييم.</summary>
    Task<bool> IsUsedAsync(Guid id, CancellationToken ct = default);

    /// <summary>يضيف وحدة بديلة واحدة لبطاقة الصنف (من سطر مستند) ويُرجع الصنف بوحداته.</summary>
    Task<ProductDto> AddUnitAsync(Guid id, ProductUnitDto unit, CancellationToken ct = default);
}
