using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface ICustomerService : ICrudService<CustomerDto, CreateCustomerDto, UpdateCustomerDto>
{
    /// <summary>يفحص صفوف ملف الاستيراد (الحقول، الهاتف، التكرار مع المسجّل وداخل الملف) دون حفظ.</summary>
    Task<PartyImportPreviewDto> PreviewImportAsync(List<PartyImportRowInput<CreateCustomerDto>> rows, CancellationToken ct = default);
    /// <summary>يعيد الفحص ويحفظ الصفوف السليمة (والتي عليها تنبيه فقط)، ويرفض صفوف الأخطاء مع أسبابها.</summary>
    Task<ImportResultDto> ImportRowsAsync(List<PartyImportRowInput<CreateCustomerDto>> rows, CancellationToken ct = default);
}
