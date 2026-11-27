namespace ERP.Core.Models.Shared;

public enum LoginOutcome
{
    Succeeded = 1,
    InvalidCredentials = 2,
    /// <summary>كلمة المرور صحيحة لأكثر من حساب (جوال مشترك): طُلب الدخول بالبريد.</summary>
    Ambiguous = 3,
    CompanyInactive = 4,
    /// <summary>طلب ناقص (بلا معرّف أو كلمة مرور).</summary>
    InvalidRequest = 5,
}

/// <summary>
/// محاولة دخول ناجحة أو فاشلة. على مستوى المنصة في القاعدة المشتركة وحدها (الدخول يسبق معرفة المنشأة، والفشل قد لا يخص منشأة):
/// بلا مرشّح منشأة، فكل قراءة تحدد `TenantId` صراحةً. المعرّف مقنَّع، ولا تُحفظ كلمة مرور أو رمز أبداً.
/// </summary>
public class LoginEvent
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }
    /// <summary>البريد أو الجوال مقنَّعاً (يكفي للمتابعة ولا يكشف الحساب كاملاً).</summary>
    public string Identifier { get; set; } = string.Empty;
    public LoginOutcome Outcome { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime OccurredAt { get; set; }
}
