using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

public class AdvanceStageRequestDto
{
    public string Stage { get; set; } = string.Empty;
    public string? Notes { get; set; }
}
