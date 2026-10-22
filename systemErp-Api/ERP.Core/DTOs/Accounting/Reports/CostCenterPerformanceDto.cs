namespace ERP.Core.DTOs.Accounting;

/// <summary>
/// أداء مراكز التكلفة في فترة: إيرادات كل مركز وتكاليفه وصافيه من حسابات الدخل وحدها.
/// ليس ميزان مراجعة: الطرف المقابل (الخزينة، الذمم، الضريبة) بلا مركز في الغالب، فلا يُفترض توازن مدين المركز ودائنه.
/// </summary>
public class CostCenterPerformanceDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    /// <summary>مرشّح المركز (يشمل فروعه)؛ فارغ = كل المراكز.</summary>
    public Guid? CostCenterId { get; set; }
    public List<CostCenterPerformanceRowDto> Rows { get; set; } = new();
    /// <summary>حركة حسابات الدخل بلا مركز تكلفة (بلا مرشّح فقط)؛ ليست ضمن مجاميع المراكز.</summary>
    public CostCenterPerformanceRowDto? Unassigned { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TotalCosts { get; set; }
    public decimal NetResult { get; set; }
}

/// <summary>مركز تكلفة بحركته المباشرة في الفترة (فروعه صفوف مستقلة يربطها ParentId).</summary>
public class CostCenterPerformanceRowDto
{
    public Guid? CostCenterId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public bool IsActive { get; set; } = true;
    public decimal Revenue { get; set; }
    public decimal CostOfSales { get; set; }
    public decimal OperatingExpenses { get; set; }
    public decimal NetResult => Revenue - CostOfSales - OperatingExpenses;

    public void Round()
    {
        Revenue = Math.Round(Revenue, 2, MidpointRounding.AwayFromZero);
        CostOfSales = Math.Round(CostOfSales, 2, MidpointRounding.AwayFromZero);
        OperatingExpenses = Math.Round(OperatingExpenses, 2, MidpointRounding.AwayFromZero);
    }
}
