using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface ISupplierService : ICrudService<SupplierDto, CreateSupplierDto, UpdateSupplierDto>
{
    /// <summary>يفحص صفوف ملف الاستيراد (الحقول، الهاتف، الآيبان، التكرار مع المسجّل وداخل الملف) دون حفظ.</summary>
    Task<PartyImportPreviewDto> PreviewImportAsync(List<PartyImportRowInput<CreateSupplierDto>> rows, CancellationToken ct = default);
    /// <summary>يعيد الفحص ويحفظ الصفوف السليمة (والتي عليها تنبيه فقط)، ويرفض صفوف الأخطاء مع أسبابها.</summary>
    Task<ImportResultDto> ImportRowsAsync(List<PartyImportRowInput<CreateSupplierDto>> rows, CancellationToken ct = default);
}
