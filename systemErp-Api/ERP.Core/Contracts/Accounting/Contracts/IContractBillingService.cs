using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;

namespace ERP.Core.Contracts.Accounting;

/// <summary>
/// فوترة عقود جدول الكميات عبر محرك الفوترة والترحيل المركزي: فاتورة الدفعة المقدمة، فاتورة المستخلص المعتمد
/// (بالخاضع والضريبة، واستقطاعاته على حساباتها فيبقى على الطرف الصافي وحده)، والإفراج عن الضمان المحتجز بقيد.
/// </summary>
public interface IContractBillingService
{
    Task<InvoiceDto> BillAdvanceAsync(Guid contractId, DateTime? date = null, CancellationToken ct = default);
    Task<InvoiceDto> BillCertificateAsync(Guid contractId, Guid certificateId, DateTime? date = null, CancellationToken ct = default);
    Task<RetentionReleaseResultDto> ReleaseRetentionAsync(Guid contractId, DateTime? date = null, CancellationToken ct = default);
}
