using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Accounting;

/// <summary>
/// نقطة الترحيل المحاسبي الوحيدة. الوحدات التشغيلية (المبيعات، المشتريات، POS، معرض السيارات) تصف
/// الأثر المالي لعملياتها فقط، وهذه الخدمة تحدّد الحسابات وتبني القيد المتوازن وتحدّث الأرصدة.
/// كل الطرق تعمل داخل معاملة المتصل وتُعيد قيداً مرحَّلاً.
/// </summary>
public interface IAccountingPostingService
{
    Task<PostingResult> PostAsync(GenericPostingRequest request, CancellationToken ct = default);
    Task<PostingResult> PostSaleAsync(SalePostingRequest request, CancellationToken ct = default);
    Task<PostingResult> PostPurchaseAsync(PurchasePostingRequest request, CancellationToken ct = default);
    Task<PostingResult> PostVoucherAsync(VoucherPostingRequest request, CancellationToken ct = default);
    /// <summary>ينشئ قيداً عكسياً ويُعلِّم الأصلي كمعكوس.</summary>
    Task<PostingResult> ReverseAsync(Guid journalEntryId, string? reason = null, CancellationToken ct = default);
    /// <summary>يستبدل أسطر قيد يدوي (يعكس أثره القديم ويطبّق الجديد على نفس رقم القيد).</summary>
    Task ReplaceManualAsync(Guid journalEntryId, GenericPostingRequest request, CancellationToken ct = default);
    Task DeleteManualAsync(Guid journalEntryId, CancellationToken ct = default);
}
