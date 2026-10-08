namespace ERP.Core.Models.Accounting;

/// <summary>
/// مسير رواتب شهر: سطر لكل موظف وقيد واحد (مصروف الرواتب وحصة المنشأة في التأمينات مقابل الرواتب المستحقة
/// والتأمينات المستحقة). لا يُعدَّل بعد ترحيله؛ يُعكس ثم يُعاد.
/// </summary>
public class PayrollRun : BaseEntity
{
    public string RunNumber { get; set; } = string.Empty;
    /// <summary>الشهر بصيغة yyyy-MM.</summary>
    public string Period { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public decimal TotalGross { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal TotalEmployeeGosi { get; set; }
    public decimal TotalEmployerGosi { get; set; }
    public decimal TotalLoanDeductions { get; set; }
    public decimal TotalNet { get; set; }
    public Guid? JournalEntryId { get; set; }
    public string Status { get; set; } = "posted"; // posted | reversed
    public string? Notes { get; set; }

    public virtual ICollection<PayrollLine> Lines { get; set; } = new List<PayrollLine>();
}

public class PayrollLine : BaseEntity
{
    public Guid PayrollRunId { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public decimal BasicSalary { get; set; }
    public decimal HousingAllowance { get; set; }
    public decimal TransportAllowance { get; set; }
    public decimal OtherAllowances { get; set; }
    /// <summary>إضافات الشهر (عمل إضافي، مكافأة).</summary>
    public decimal Additions { get; set; }
    /// <summary>خصومات الشهر (غياب، سلفة، جزاء).</summary>
    public decimal Deductions { get; set; }
    public decimal UnpaidLeaveDays { get; set; }
    public decimal LeaveDeduction { get; set; }
    /// <summary>أقساط السلف المخصومة من الصافي (سداد لا مصروف).</summary>
    public decimal LoanDeduction { get; set; }
    public decimal Gross { get; set; }
    public decimal EmployeeGosi { get; set; }
    public decimal EmployerGosi { get; set; }
    public decimal Net { get; set; }
    public Guid? CostCenterId { get; set; }
}
