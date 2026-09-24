using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

public class ReportQueryDto
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public Guid? ItemId { get; set; }
    public string? Category { get; set; }
}
