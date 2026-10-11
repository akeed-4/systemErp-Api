using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.POS;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.POS;

public interface IPosSettingsService
{
    Task<PosInvoiceSettingsDto> GetAsync(CancellationToken ct = default);
    Task<PosInvoiceSettingsDto> UpdateAsync(UpdatePosInvoiceSettingsDto request, CancellationToken ct = default);
}
