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
    /// <summary>العقد المبسط: تقديم المستخلص واعتماده وفوترته في خطوة واحدة.</summary>
    Task<InvoiceDto> IssueCertificateAsync(Guid contractId, Guid certificateId, DateTime? date = null, CancellationToken ct = default);
    Task<RetentionReleaseResultDto> ReleaseRetentionAsync(Guid contractId, DateTime? date = null, CancellationToken ct = default);
    /// <summary>تحصيل/سداد فاتورة عقد (مستخلص أو دفعة مقدمة) بسند قبض/صرف موزَّع عليها؛ على أي مسار (مبسط أو كامل).</summary>
    Task<VoucherDto> RecordPaymentAsync(Guid contractId, ContractPaymentRequestDto request, CancellationToken ct = default);
}
