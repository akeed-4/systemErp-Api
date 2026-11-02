using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>إعداد ترقيم أنواع المستندات (يدوي/تلقائي، البادئة، الطول، النطاق، التصفير السنوي، الرقم التالي).</summary>
public interface IDocumentNumberingService
{
    /// <summary>كل الأنواع بإعدادها وعدّادها (للمستودع المحدد عند الترقيم لكل مستودع، وللسنة المالية الحالية).</summary>
    Task<List<DocumentNumberingSettingDto>> ListAsync(Guid? warehouseId, CancellationToken ct = default);
    Task<DocumentNumberingSettingDto> UpdateAsync(string key, UpdateDocumentNumberingDto dto, CancellationToken ct = default);
    /// <summary>أنواع المستندات المضبوطة يدوياً (لتُظهر الشاشات حقل الرقم).</summary>
    Task<List<string>> ManualTypesAsync(CancellationToken ct = default);
}
