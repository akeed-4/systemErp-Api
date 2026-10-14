namespace ERP.Core.DTOs.Shared;

public class RecalculateCostingResultDto
{
    public int ProductsUpdated { get; set; }
    public CostingMethod Method { get; set; }
    /// <summary>فرق قيمة المخزون الناتج عن إعادة الاحتساب (موجب = زيادة)، مرحَّلاً بقيد إعادة تقييم.</summary>
    public decimal RevaluationAmount { get; set; }
    public string? RevaluationEntryNumber { get; set; }
}
