namespace ERP.Core.Models.Shared;

/// <summary>دعوة تفعيل حساب (رابط يعيّن منه المستخدم كلمة مروره). الرمز يُخزَّن مجزّأً (hash) ويُستعمل مرة واحدة.</summary>
public class UserInvite : BaseEntity
{
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
}
