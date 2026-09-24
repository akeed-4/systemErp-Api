using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Accounting;

public interface IZatcaService
{
    /// <summary>ينشئ رمز QR وفق TLV المرحلة الأولى (Base64) للفاتورة.</summary>
    string GenerateTlvQr(string sellerName, string vatNumber, DateTime timestamp, decimal total, decimal vat);
    Task<ZatcaSubmitResultDto> SubmitInvoiceAsync(Guid invoiceId, CancellationToken ct = default);
}
