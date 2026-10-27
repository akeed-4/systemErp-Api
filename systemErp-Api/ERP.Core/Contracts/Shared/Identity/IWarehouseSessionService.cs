using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>موقع الجلسة: يُضبط لكل طلب من مواقع المستخدم المسموحة والموقع المختار عند الدخول (ترويسة X-Warehouse-Id).</summary>
public interface IWarehouseSessionService
{
    /// <summary>يحدّد موقع الطلب ويضبطه على سياق البيانات (يستدعيه وسيط الجلسة).</summary>
    Task InitializeAsync(CancellationToken ct = default);
    Task<WarehouseSessionDto> GetAsync(CancellationToken ct = default);
    Task<WarehouseSessionDto> SetDocumentVisibilityAsync(string visibility, CancellationToken ct = default);
}
