namespace ERP.Core.Models.Accounting;

public class CostCenter : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>المركز الأب (هرم اختياري)؛ تقارير المركز تشمل مراكزه الفرعية.</summary>
    public Guid? ParentId { get; set; }
}
