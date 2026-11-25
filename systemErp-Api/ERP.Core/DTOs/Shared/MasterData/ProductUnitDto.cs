namespace ERP.Core.DTOs.Shared;

/// <summary>وحدة بديلة للصنف: رمزها من دليل الوحدات ومعامل تحويلها إلى وحدته الأساسية (1 منها = المعامل من الأساسية).</summary>
public class ProductUnitDto
{
    public Guid Id { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public decimal Factor { get; set; } = 1m;
    public int SortOrder { get; set; }
}
