namespace ERP.Core.Models.Accounting;

/// <summary>قسم أو إدارة في الهيكل التنظيمي يُنسب إليها الموظفون.</summary>
public class Department : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    /// <summary>مدير القسم (موظف)، اختياري.</summary>
    public Guid? ManagerEmployeeId { get; set; }
    public bool IsActive { get; set; } = true;
}
