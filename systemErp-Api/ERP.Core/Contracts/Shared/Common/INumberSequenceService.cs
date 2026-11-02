using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>يُنتج أرقام المستندات المتسلسلة لكل منشأة (فاتورة، قيد، سند، ...).</summary>
public interface INumberSequenceService
{
    /// <summary>الرقم التالي، مثال: SINV-000001. يجب استدعاؤه داخل نفس معاملة المستند.</summary>
    Task<string> NextAsync(string key, string defaultPrefix, CancellationToken ct = default);
    /// <summary>الرقم التالي لمستند في مستودع بعينه (لنطاق الترقيم لكل مستودع)؛ فارغ = مستودع الجلسة.</summary>
    Task<string> NextAsync(string key, string defaultPrefix, Guid? warehouseId, CancellationToken ct = default);
    /// <summary>
    /// رقم مستند بإعداد نوعه: يدوي (نوع يدعمه، مضبوط يدوياً، وطلب من المستخدم) بعد التحقق من صيغته وتكراره،
    /// وإلا تلقائي بالبادئة والطول والنطاق والسنة المالية لتاريخ المستند. يجب استدعاؤه داخل نفس معاملة المستند.
    /// </summary>
    Task<string> AssignAsync(DocumentNumberRequest request, CancellationToken ct = default);
    /// <summary>العدّاد الذي سيصرف منه مستند بهذه البيانات: مفتاحه وبادئته وقيمته والرقم التالي المتوقع (بلا صرف).</summary>
    Task<DocumentCounterInfo> CounterAsync(string key, string defaultPrefix, Guid? warehouseId, DateTime? date, CancellationToken ct = default);
    /// <summary>يحجز كتلة من <paramref name="count"/> أرقام متتالية دفعة واحدة ويعيد أول قيمة فيها.</summary>
    Task<long> ReserveAsync(string key, string defaultPrefix, int count, CancellationToken ct = default);
}
