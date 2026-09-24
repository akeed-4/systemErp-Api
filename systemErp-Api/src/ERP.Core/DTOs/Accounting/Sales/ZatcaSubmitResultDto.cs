using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

public class ZatcaSubmitResultDto
{
    public bool Success { get; set; }
    public ZatcaSubmissionStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? QrCode { get; set; }
}
