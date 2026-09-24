namespace ERP.Core.DTOs.Shared;

public class VerifyOtpRequestDto
{
    public Guid UserId { get; set; }
    public string Otp { get; set; } = string.Empty;
}
