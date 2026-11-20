namespace ERP.Core.Models.Shared;

/// <summary>مزودو تخزين محتوى المرفقات. السجل يحفظ مزوده فيُقرأ منه دائماً ولو تغيّر المزود الافتراضي لاحقاً.</summary>
public static class AttachmentStorageProviders
{
    /// <summary>المحتوى في جدول <see cref="DocumentAttachmentContent"/> بقاعدة بيانات المنشأة نفسها.</summary>
    public const string Database = "db";
}

/// <summary>تصنيفات المرفق المشترك. المالك قد يحصرها في بعضها (<c>IAttachmentOwner.Categories</c>).</summary>
public static class DocumentAttachmentCategories
{
    public const string Photo = "photo";
    public const string Damage = "damage";
    public const string Inspection = "inspection";
    public const string Signature = "signature";
    public const string Document = "document";
    public const string Other = "other";

    public static readonly string[] All = { Photo, Damage, Inspection, Signature, Document, Other };
}

/// <summary>
/// مرفق مشترك لمستندات الوحدات الجديدة (الورش والتأجير): صور الاستلام والتسليم والأضرار والفحص والتوقيع والمستندات المؤيدة.
/// يشير لمالكه بنوعه ومعرّفه (<see cref="OwnerType"/>/<see cref="OwnerId"/>، مرجع متعدد الأنواع بلا مفتاح أجنبي)، والمحتوى منفصل عنه
/// عند مزوده (<see cref="StorageProvider"/>/<see cref="StorageKey"/>) فلا تُحمَّل البيانات الثنائية مع القوائم. مرفقات المقاولات والإجازات
/// القائمة تبقى في جداولها.
/// </summary>
public class DocumentAttachment : BaseEntity
{
    /// <summary>نوع المالك كما يسجّله معالجه (مثل <c>work_order</c> أو <c>rental_handover</c>).</summary>
    public string OwnerType { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public string Category { get; set; } = DocumentAttachmentCategories.Other;
    public string? Note { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    /// <summary>SHA-256 للمحتوى (hex): للتحقق من سلامته عند القراءة من مزود خارجي، وكشف الرفع المكرر.</summary>
    public string Checksum { get; set; } = string.Empty;
    public string StorageProvider { get; set; } = AttachmentStorageProviders.Database;
    /// <summary>مفتاح المحتوى عند مزوده (لمزود قاعدة البيانات: معرّف سطر المحتوى).</summary>
    public string StorageKey { get; set; } = string.Empty;
    /// <summary>وقت الالتقاط إن أرسله الجهاز (صورة الاستلام مثلاً)؛ فارغ = وقت الرفع.</summary>
    public DateTime? CapturedAt { get; set; }
    public string? UploadedBy { get; set; }
    public Guid? UploadedByUserId { get; set; }
}

/// <summary>محتوى المرفق لمزود قاعدة البيانات، سطر واحد لكل مرفق.</summary>
public class DocumentAttachmentContent : BaseEntity
{
    public Guid AttachmentId { get; set; }
    public byte[] Content { get; set; } = Array.Empty<byte>();
}
