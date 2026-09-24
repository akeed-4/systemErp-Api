namespace ERP.Core.DTOs.Shared;

public class ForgotPasswordRequestDto
{
    /// <summary>بريد أو جوال المستخدم.</summary>
    public string Identifier { get; set; } = string.Empty;
}
