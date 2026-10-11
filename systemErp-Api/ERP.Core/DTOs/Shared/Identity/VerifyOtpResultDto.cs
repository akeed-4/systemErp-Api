namespace ERP.Core.DTOs.Shared;

public class VerifyOtpResultDto
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? ResetToken { get; set; }
}
