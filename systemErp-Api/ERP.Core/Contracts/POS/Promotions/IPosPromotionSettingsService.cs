using ERP.Core.DTOs.POS;

namespace ERP.Core.Contracts.POS;

public interface IPosPromotionSettingsService
{
    Task<PosPromotionSettingsDto> GetAsync(CancellationToken ct = default);
    Task<PosPromotionSettingsDto> UpdateAsync(UpdatePosPromotionSettingsDto request, CancellationToken ct = default);
}
