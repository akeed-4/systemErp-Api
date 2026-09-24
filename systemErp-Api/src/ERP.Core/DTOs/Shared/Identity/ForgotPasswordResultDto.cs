namespace ERP.Core.DTOs.Shared;

public class ForgotPasswordResultDto
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string? MaskedEmail { get; set; }
    public string? MaskedPhone { get; set; }
    /// <summary>يُعاد فقط في بيئات التطوير (Auth:ExposeOtpInResponse) ولا يُعاد أبداً في الإنتاج.</summary>
    public string? OtpCode { get; set; }
    public int ExpiresInSeconds { get; set; }
}
