namespace ERP.Domain.Common;

/// <summary>
/// الأساس المشترك لجميع الكيانات: معرّف فريد، عزل المستأجر، وطوابع الإنشاء/التعديل.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
