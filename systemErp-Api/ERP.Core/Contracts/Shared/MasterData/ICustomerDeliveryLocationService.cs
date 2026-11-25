using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>مواقع توصيل العميل (ترث صلاحية البيانات الأساسية <c>master-data</c>).</summary>
public interface ICustomerDeliveryLocationService
{
    Task<List<CustomerDeliveryLocationDto>> ListAsync(Guid customerId, bool includeInactive, CancellationToken ct = default);
    Task<CustomerDeliveryLocationDto> CreateAsync(Guid customerId, SaveCustomerDeliveryLocationDto request, CancellationToken ct = default);
    Task<CustomerDeliveryLocationDto> UpdateAsync(Guid customerId, Guid id, SaveCustomerDeliveryLocationDto request, CancellationToken ct = default);
    /// <summary>يحذف الموقع غير المستخدم، ويعطّل المستخدم في بيان تسليم. يعيد true إن حُذف.</summary>
    Task<bool> DeleteAsync(Guid customerId, Guid id, CancellationToken ct = default);
}

/// <summary>وكيل البحث الجغرافي المجاني (مزود من الإعدادات، بحد طلبات وتخزين مؤقت). معطّل = إدخال يدوي فقط.</summary>
public interface IGeocodingService
{
    bool Enabled { get; }
    Task<List<GeocodeResultDto>> SearchAsync(string query, CancellationToken ct = default);
}
