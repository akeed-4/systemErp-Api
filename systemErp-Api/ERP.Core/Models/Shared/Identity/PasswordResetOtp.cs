namespace ERP.Core.Models.Shared;

/// <summary>رمز تحقق لاستعادة كلمة المرور. يُخزَّن مجزّأً (hash) ولا يُعاد أبداً بعد إنشائه.</summary>
public class PasswordResetOtp : BaseEntity
{
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public DateTime? UsedAt { get; set; }
}
