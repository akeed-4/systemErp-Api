namespace ERP.Core.Models.Shared;

/// <summary>
/// الأساس المشترك لجميع الكيانات: معرّف فريد، عزل المستأجر، وطوابع الإنشاء/التعديل.
/// </summary>
public abstract class BaseEntity
{
    /// <summary>يُولَّد عند الإضافة (Guid.Empty = كيان جديد). لا يُعيَّن مسبقاً حتى يُميّز EF الكيانات الجديدة المضافة إلى أب متتبَّع.</summary>
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
