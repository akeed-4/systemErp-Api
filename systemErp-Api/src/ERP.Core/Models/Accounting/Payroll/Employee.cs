namespace ERP.Core.Models.Accounting;

/// <summary>موظف وبنود راتبه الشهري الثابتة. نسب التأمينات تُضبط لكل موظف (تختلف بحسب الجنسية وتاريخ الالتحاق).</summary>
public class Employee : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string? NationalId { get; set; }
    public string? JobTitle { get; set; }
    public DateTime HireDate { get; set; }
    public decimal BasicSalary { get; set; }
    public decimal HousingAllowance { get; set; }
    public decimal TransportAllowance { get; set; }
    public decimal OtherAllowances { get; set; }
    /// <summary>نسبة حصة الموظف في التأمينات الاجتماعية (%) من الأساسي + السكن.</summary>
    public decimal EmployeeGosiRate { get; set; }
    /// <summary>نسبة حصة المنشأة في التأمينات الاجتماعية (%) من الأساسي + السكن.</summary>
    public decimal EmployerGosiRate { get; set; }
    public string? Iban { get; set; }
    public Guid? CostCenterId { get; set; }
    public string Status { get; set; } = "active"; // active | inactive
}
