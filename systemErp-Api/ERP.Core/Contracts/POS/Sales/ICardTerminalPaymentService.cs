using ERP.Core.DTOs.POS;

namespace ERP.Core.Contracts.POS;

/// <summary>
/// دفعات جهاز الدفع في نقطة البيع. السرفر لا يتصل بالجهاز: برنامج الربط على جهاز الكاشير يرسل المبلغ للجهاز وينقل نتيجته هنا،
/// والإتمام (CheckoutAsync) لا يقبل طريقة مفعّلاً لها الجهاز إلا بدفع مُوافَق عليه غير مستخدم بالمبلغ نفسه.
/// </summary>
public interface ICardTerminalPaymentService
{
    /// <summary>ينشئ دفعاً Pending بمرجع فريد في وردية الكاشير المفتوحة؛ يُرفض ما دام في الوردية دفع لم تُحسم نتيجته.</summary>
    Task<CardTerminalPaymentDto> StartAsync(StartCardTerminalPaymentDto request, CancellationToken ct = default);
    /// <summary>نتيجة الجهاز (أو نتيجة الاستعلام بعد الانقطاع). النتيجة النهائية لا تُستبدل.</summary>
    Task<CardTerminalPaymentDto> ReportAsync(Guid id, ReportCardTerminalResultDto request, CancellationToken ct = default);
    /// <summary>حسم إداري موثَّق لدفع معلّق/مجهول، أو عكس موافقة لم تُستخدم.</summary>
    Task<CardTerminalPaymentDto> ResolveAsync(Guid id, ResolveCardTerminalPaymentDto request, CancellationToken ct = default);
    Task<CardTerminalPaymentDto> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>دفعات وردية (الافتراضي: وردية الكاشير المفتوحة)؛ unresolved = المعلّقة والمجهولة والموافقات غير المستخدمة.</summary>
    Task<List<CardTerminalPaymentDto>> ListAsync(Guid? shiftId, bool unresolvedOnly, CancellationToken ct = default);
}
