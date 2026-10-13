using ERP.Core.DTOs.CarShowroom;

namespace ERP.Core.Contracts.CarShowroom;

public interface IAdditionalChargeService
{
    /// <summary>أسطر مستند (documentType + documentId) أو أسطر سيارة (vehicleId). من لا يرى التكلفة يرى الأسطر المحمّلة على العميل فقط.</summary>
    Task<List<AdditionalChargeLineDto>> ListAsync(string? documentType, Guid? documentId, Guid? vehicleId, CancellationToken ct = default);
    Task<AdditionalChargeLineDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<AdditionalChargeLineDto> CreateAsync(CreateAdditionalChargeLineDto dto, CancellationToken ct = default);
    Task<AdditionalChargeLineDto> UpdateAsync(Guid id, UpdateAdditionalChargeLineDto dto, CancellationToken ct = default);
    /// <summary>يلغي السطر مع إبقائه في السجل (يخرج من تكلفة السيارة).</summary>
    /// <param name="reason">إلزامي حين يمسّ الإجراء تكلفة سيارة مباعة (قيد تعديل موثق).</param>
    Task<AdditionalChargeLineDto> CancelAsync(Guid id, string? reason = null, CancellationToken ct = default);
    Task DeleteAsync(Guid id, string? reason = null, CancellationToken ct = default);
}
