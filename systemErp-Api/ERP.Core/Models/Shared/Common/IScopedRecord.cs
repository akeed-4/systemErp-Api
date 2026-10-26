namespace ERP.Core.Models.Shared;

/// <summary>
/// مستند يخضع لنطاق الوصول: يُختم منشئه تلقائياً عند الحفظ، ويمكن إسناده لمستخدم.
/// الفرع = المستودع (لا كيان فروع مستقل في النظام)، ويُقرأ من خاصية WarehouseId إن وُجدت.
/// </summary>
public interface IScopedRecord
{
    Guid? CreatedByUserId { get; set; }
    Guid? AssignedToUserId { get; set; }
}
