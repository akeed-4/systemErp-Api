namespace ERP.Core.Models.Shared;

/// <summary>
/// وحدة بديلة مرتبطة بالصنف (كرتون، علبة، درزن...) بمعامل تحويلها إلى وحدة الصنف الأساسية <see cref="Product.Unit"/>:
/// 1 من هذه الوحدة = <see cref="Factor"/> من الوحدة الأساسية. المخزون والتكلفة بالوحدة الأساسية دائماً، والوحدة البديلة
/// للإدخال في المستند وحده (سطر الفاتورة يحفظ وحدته ومعاملها لقطةً فلا يغيّره تعديل الصنف لاحقاً).
/// </summary>
public class ProductUnit : BaseEntity
{
    public Guid ProductId { get; set; }
    public virtual Product Product { get; set; } = null!;
    /// <summary>رمز الوحدة من دليل الوحدات <see cref="UnitOfMeasure.Code"/>.</summary>
    public string UnitCode { get; set; } = string.Empty;
    /// <summary>كم وحدة أساسية في وحدة واحدة من هذه (أكبر من صفر، ولا تساوي 1 إلا لوحدة بالحجم نفسه).</summary>
    public decimal Factor { get; set; } = 1m;
    public int SortOrder { get; set; }
}
