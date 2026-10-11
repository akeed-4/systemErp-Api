namespace ERP.Core.DTOs.Shared;

/// <summary>تغيير كلمة مرور المستخدم الحالي (يتطلب كلمة المرور الحالية).</summary>
public class ChangePasswordRequestDto
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}