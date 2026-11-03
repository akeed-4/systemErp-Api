using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>
/// بوابة الاعتماد للعمليات المحجوزة: إن انطبقت سياسة اعتماد على عملية مستند (إنشاء/تعديل/حذف/ترحيل...) تُحفظ العملية
/// بطلب اعتماد ولا تُنفَّذ؛ ويُنفّذها السرفر نفسه عند الاعتماد النهائي، فلا أثر لها في الدفاتر أو المخزون قبل ذلك.
/// </summary>
public interface IApprovalGate
{
    /// <summary>
    /// يحجز العملية إن انطبقت سياسة (بعد تجربتها داخل معاملة تُلغى للتحقق وبناء ملخص المعتمِد) ويعيد طلبها؛
    /// وإلا null فتُنفَّذ العملية مباشرة.
    /// </summary>
    Task<ApprovalRequestDto?> TryHoldAsync(string handler, string action, Guid? documentId, object? payload, CancellationToken ct = default);

    /// <summary>ينفّذ عملية محجوزة بعد اعتمادها النهائي (داخل معاملة القرار)، ويعيد معرّف المستند الناتج ورقمه.</summary>
    Task<(Guid DocumentId, string? DocumentNumber)> ExecuteAsync(ApprovalDeferredOperation deferred, CancellationToken ct = default);

    /// <summary>فهرس المستندات والعمليات القابلة للاعتماد مجمّعاً بالوحدات.</summary>
    ApprovalDocumentCatalogDto Catalog();
}
