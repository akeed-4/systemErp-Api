using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>
/// المرفقات المشتركة (صور ومستندات) لأي مستند تسجّل وحدته معالجاً له (<see cref="IAttachmentOwner"/>). الخدمة تفرض الترخيص وصلاحية العرض
/// على شاشة المالك ونوع الملف وحجمه وعدده، والمعالج يقرر وجود المستند ونطاقه ومن يضيف ومن يحذف بحسب حالته.
/// </summary>
public interface IDocumentAttachmentService
{
    Task<DocumentAttachmentSummaryDto> SummaryAsync(string ownerType, Guid ownerId, CancellationToken ct = default);
    Task<List<DocumentAttachmentDto>> ListAsync(string ownerType, Guid ownerId, CancellationToken ct = default);
    Task<DocumentAttachmentDto> AddAsync(string ownerType, Guid ownerId, AddDocumentAttachmentRequest request, string fileName, byte[] content,
        CancellationToken ct = default);
    Task<DocumentAttachmentFileDto> GetFileAsync(Guid attachmentId, CancellationToken ct = default);
    Task DeleteAsync(Guid attachmentId, CancellationToken ct = default);
}

/// <summary>
/// معالج نوع مالك للمرفقات تسجّله الوحدة (يُكتشف تلقائياً بالحقن). يتحقق من وجود المستند في المنشأة ونطاق المستخدم (يرمي
/// <c>NotFoundException</c> أو <c>ForbiddenException</c>) ويعيد ما يملكه المستخدم عليه.
/// </summary>
public interface IAttachmentOwner
{
    /// <summary>نوع المالك الثابت (حروف صغيرة و_)، فريد بين المعالجات.</summary>
    string OwnerType { get; }
    /// <summary>وحدة المنصة التي يتطلبها (<c>PlatformModules</c>).</summary>
    string Module { get; }
    /// <summary>شاشة الصلاحيات التي يتطلب العرض عليها.</summary>
    string ScreenId { get; }
    /// <summary>التصنيفات المسموحة لهذا المالك (من <c>DocumentAttachmentCategories</c>).</summary>
    IReadOnlyCollection<string> Categories { get; }
    int MaxPerOwner { get; }
    long MaxBytes { get; }
    Task<AttachmentOwnerAccess> ResolveAsync(Guid ownerId, CancellationToken ct = default);
}

/// <param name="AuditEntity">اسم الكيان في سجل التدقيق.</param>
/// <param name="AuditLabel">وصف المستند في سجل التدقيق (رقمه مثلاً).</param>
public sealed record AttachmentOwnerAccess(string AuditEntity, string AuditLabel, bool CanAdd, bool CanDelete);

/// <summary>
/// مزود تخزين محتوى المرفقات. السجل يحفظ اسم مزوده ومفتاحه، فيمكن إضافة مزود (ملفات أو Blob) دون نقل القائم.
/// مزود قاعدة البيانات يضيف المحتوى للسياق ويحفظه المستدعي في معاملته؛ المزود الخارجي يكتب فوراً.
/// </summary>
public interface IAttachmentStorage
{
    string Provider { get; }
    /// <summary>يخزّن المحتوى ويعيد مفتاحه.</summary>
    Task<string> StoreAsync(Guid attachmentId, byte[] content, CancellationToken ct = default);
    Task<byte[]> ReadAsync(string key, CancellationToken ct = default);
    /// <summary>يحذف المحتوى (مزود قاعدة البيانات: يعلّمه للحذف ويحفظه المستدعي).</summary>
    Task RemoveAsync(string key, CancellationToken ct = default);
}
