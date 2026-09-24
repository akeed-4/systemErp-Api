namespace ERP.Core.DTOs.Shared;

public class ResetPasswordRequestDto
{
    public Guid UserId { get; set; }
    public string Otp { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
